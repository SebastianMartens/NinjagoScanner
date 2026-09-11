## Context

PictureService today (C#, ASP.NET Core, `Grpc.AspNetCore`) is a thin, well-isolated seam: it owns an S3 bucket (photo bytes, keyed `photos/{collectionId}/{photoId}`), a DynamoDB table (sidecar records, partition key `CollectionId`, sort key `PhotoId`, one attribute per sidecar field in PascalCase — e.g. `AnalysisStatus`, `CardNumber`, `DetectedText` stored as a DynamoDB `List` rather than a `Set` because OCR text can contain duplicates), a single outbound Gemini HTTP call, and a gRPC client to `NinjagoScanner.CatalogService`. Its own gRPC surface (`Protos/picture_service.proto`) is called only by `NinjagoScanner.Web`. See proposal.md for why this is being ported to Python now rather than alongside the pipeline redesign.

## Goals / Non-Goals

**Goals:**
- Byte-for-byte behavioral parity with the 13 existing specs (`picture-service-*`, `collection-scoped-picture-access`).
- Fully async implementation (`grpc.aio`, `aioboto3`) from the start.
- Zero required changes to `NinjagoScanner.Web` or `NinjagoScanner.CatalogService` — the gRPC contract is the only coupling.
- A Web test suite that no longer depends on PictureService's implementation language or runtime.

**Non-Goals:**
- The multi-step extraction/identification pipeline, LangGraph branching, LangSmith tracing/evaluation — explicitly deferred to a follow-up change.
- Performance improvements as a goal in themselves (async I/O may help incidentally, but this is not a performance change).
- Any change to how `NinjagoScanner.CatalogService` or `NinjagoScanner.Web` are hosted.

## Decisions

**Fully async from the start (`grpc.aio`).** `UploadPhoto` is a client-streaming RPC and the service is I/O-bound end to end (Gemini call, S3, DynamoDB); async lets one worker handle concurrent uploads without a thread-per-call model. Alternative considered: sync `grpcio` server with a thread pool — rejected as strictly worse once `aioboto3` is already in play, since it would reintroduce blocking calls the async server elsewhere avoids.

**`aioboto3` over `boto3` for S3/DynamoDB.** Gives real async S3/DynamoDB calls consistent with the async server, at the cost of depending on a community-maintained wrapper rather than official `boto3`. Alternative considered: sync `boto3` wrapped in `asyncio.to_thread` per call — more "official" but reintroduces blocking and per-call thread-pool overhead; rejected given current call volume doesn't need `boto3`-proper's guarantees and the wrapper only covers a small, stable slice of the S3/DynamoDB API surface actually used here (get/put/delete object, get/put/delete/query/scan item).

**`langchain-google-genai` for the Gemini call, with a hand-rolled retry wrapper.** Structured output (`with_structured_output` against a pydantic schema mirroring the existing JSON schema) replaces the current hand-rolled prompt-schema-plus-manual-parse in `GeminiApiService.ParseSuccessResponse` — a real simplification. However, `picture-service-gemini-analysis` specifies an exact retry policy (retry only on HTTP 429/5xx, wait `retry_delay_ms * attempt`, fail immediately on any other status, and tag every failure as transport- vs. content-level). LangChain's built-in `.with_retry()` is a generic exponential-backoff-on-any-exception policy and won't reproduce this; the retry loop and failure classification stay custom code wrapping the LangChain call rather than relying on the library for it.

**Preserve the exact DynamoDB item shape and S3 key layout.** Same partition/sort key names (`CollectionId`/`PhotoId`), same PascalCase attribute names per `SidecarRecord` field, `DetectedText` written as a DynamoDB `List` (never `Set`), `ScannedAtUtc` as an ISO-8601 string in the same round-trippable format, and the same S3 key prefix (`photos/{collectionId}/{photoId}`). This is a hard constraint, not a style choice: it's what makes the cutover a same-data deploy instead of a data migration (see Migration Plan). Alternative considered: a cleaner Python-native schema (e.g. snake_case, a single JSON blob per item) with a one-time backfill — rejected as unnecessary complexity and migration risk for a change whose entire point is "no behavior change."

**Hand-written C# fake `CardPictureServiceBase` in `NinjagoScanner.Web.Tests`, replacing the in-process real host.** Decouples Web's test suite from PictureService's language and runtime entirely — the only shared contract becomes the `.proto` file, and `dotnet test NinjagoScanner.Web.Tests` no longer needs a Python interpreter, process spawning, or port/readiness handling. Trade-off: the fake can drift from the real Python service's behavior since nothing but the shared spec text keeps them aligned — see Risks.

**`picture_service/` at the repo root, `uv`-managed, outside `NinjagoScanner.slnx`.** Matches the existing flat layout (sibling to the `.NET` project folders) without forcing a Python package into a .NET-only naming convention or an artificial `services/` nesting. `uv` was chosen for being a fast, single-tool, increasingly-default choice for new Python projects.

**Proto stubs generated at build time via `grpcio-tools`, not committed.** Mirrors `Grpc.Tools`' existing behavior for the C# projects — the `.proto` files stay the single source of truth on both sides of the language boundary, and generated code can't silently go stale in git.

## Risks / Trade-offs

- **[Risk]** The hand-written C# fake in `Web.Tests` drifts from the real Python service's behavior over time, since only the shared spec text (not a shared test suite or shared code) keeps them aligned → **Mitigation:** the Python side's own pytest suite should assert the same scenarios already written in `openspec/specs/picture-service-*/spec.md` directly against the real implementation, so both suites check the same source of truth even though they exercise different servers.
- **[Risk]** A DynamoDB/S3 data-shape mismatch (wrong attribute name, `Set` instead of `List`, a different timestamp format) would make the new service unable to read sidecars written by the old one, or write records the old one couldn't parse during a rollback → **Mitigation:** covered by the Decisions above; verify by round-tripping against a handful of real production sidecar items before cutover, not just fixture data.
- **[Risk]** `langchain-google-genai`'s structured-output parsing may handle malformed/partial model output differently than the current manual `System.Text.Json` parse → **Mitigation:** exercise every scenario in `picture-service-gemini-analysis/spec.md` against the new implementation, including empty-candidate-text, invalid-JSON, and confidence-clamping edge cases.
- **[Risk]** `aioboto3` is a community wrapper over `aiobotocore`, not an official AWS package, and can lag official `boto3` releases → **Mitigation:** acceptable given the narrow, stable API surface actually used; revisit if it becomes unmaintained.
- **[Risk]** The .NET OTel exporter needed an explicit `HttpProtobuf` override because its SDK defaults to gRPC, which Grafana Cloud's OTLP gateway silently rejects (see `feedback_flyctl_gotchas` memory) — the Python OTel SDK's defaults are not guaranteed to match → **Mitigation:** re-verify the Python exporter's default protocol against Grafana Cloud explicitly; don't assume parity with the .NET workaround.
- **[Trade-off]** Python has no `InternalsVisibleTo` equivalent for hiding implementation types from other packages → module/package structure and naming convention (leading underscore) take its place; not a functional risk, just a style adjustment.

## Migration Plan

Because the gRPC contract, S3 key layout, and DynamoDB item shape are all preserved exactly, this is a same-slot redeploy, not a data migration:

1. Build and test the Python service against the existing specs using a scratch S3 bucket/DynamoDB table.
2. Round-trip a handful of real production sidecar items through the new service's read/write path to confirm data-shape compatibility.
3. Deploy the Python image to the existing `picture-service` Fly app (same app name / `.internal` DNS address), during a low-traffic window — `NinjagoScanner.Web`'s configured PictureService address does not change.
4. Manually exercise each RPC once against production storage post-deploy (a sample `Scan`, an `UploadPhoto`, `ListCards`, a review-status update) and watch the existing OTel/Grafana Cloud traces for errors.
5. **Rollback:** Fly retains prior releases; `fly releases rollback` to the last C# image is safe in either direction since no data format changed.
6. Remove the C# `NinjagoScanner.PictureService`/`.Tests` projects and their `NinjagoScanner.slnx` entries only after the Python service has run in production through a bake-in period — as a separate follow-up commit, not bundled with the cutover — so a plain `git revert` remains available under time pressure without resurrecting deleted code from history.
