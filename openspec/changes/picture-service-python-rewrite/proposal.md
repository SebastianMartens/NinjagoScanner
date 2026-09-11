## Why

We want to evolve card analysis into a more sophisticated, evaluable multi-step AI pipeline (extraction, then identification, with branching per card layout) built on LangGraph, with LangSmith for tracing and per-attribute accuracy evaluation against a golden set of verified photos. Building that natively in Python — where these tools are first-class — is preferable to bolting a thin LangChain wrapper onto a single C# call site or standing up a fourth, narrowly-scoped service. Rather than mixing the language migration with the pipeline redesign, this change ports PictureService to Python first, as a pure reimplementation with no behavior change, so the pipeline work (a separate, later change) lands directly in the target language and tooling.

## What Changes

- Replace `NinjagoScanner.PictureService` (C#/ASP.NET Core/Grpc.AspNetCore) with a new `picture_service/` (Python, `grpc.aio`, fully async) implementing the exact same `CardPictureService` gRPC contract (`Protos/picture_service.proto`, unchanged) and the exact same observable behavior across every existing `picture-service-*` and `collection-scoped-picture-access` spec.
- Port the Gemini API integration from a hand-rolled `HttpClient` call (`GeminiApiService.cs`) to `langchain-google-genai`'s structured-output chat model call. This is the one implementation-approach change in the port; the request content, retry policy (retry only on 429/5xx, `retry_delay_ms * attempt` backoff, transport-vs-content failure distinction), and parsed result shape are preserved 1:1, wrapped by hand where LangChain's built-in retry doesn't match that exact policy.
- Port S3/DynamoDB access from the AWS SDK for .NET to `aioboto3`.
- Port `SidecarCache`, `SeriesCatalogService`, the `CatalogGrpcClient` (calling `NinjagoScanner.CatalogService` over gRPC, unchanged on that end), and all sidecar/photo/scan RPC handling mechanically, file-for-file, into async Python.
- Replace `NinjagoScanner.PictureService.Tests` (C# xunit) with an equivalent Python pytest suite using in-memory fakes for the photo store and sidecar store.
- **BREAKING** (internal-only, no external contract change): remove the C# `PictureScannerGrpcService` class and the `NinjagoScanner.PictureService.Tests` project. Replace `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs` (which hosted a real in-process `PictureScannerGrpcService`) with a hand-written C# fake implementing the generated `CardPictureServiceBase`. Web's test suite becomes decoupled from PictureService's implementation language entirely — the only shared contract is the `.proto` file.
- Update deployment: new Dockerfile and Fly.io app definition for the Python service; rewrite `.github/workflows/deploy-picture-service.yml` for a Python build/test/publish pipeline.
- Update local dev: add the Python service to the VS Code compound launch config alongside the two remaining .NET services.
- Remove `NinjagoScanner.PictureService` and `NinjagoScanner.PictureService.Tests` from `NinjagoScanner.slnx` (a Python project cannot be a member of a .NET solution); update CLAUDE.md's Commands section to document the separate `uv run` / pytest invocation for `picture_service/`.

## Capabilities

### New Capabilities

None. This change introduces no new observable behavior.

### Modified Capabilities

None. Every existing `picture-service-*` capability (`picture-service-gemini-analysis`, `-photo-scan`, `-photo-storage`, `-photo-upload`, `-photo-download`, `-photo-deletion`, `-card-listing`, `-sidecar-cache`, `-sidecar-editing`, `-sidecar-review`, `-series-name-matching`, `-directory-resolution`) and `collection-scoped-picture-access` is expected to hold, unchanged, against the new Python implementation — this is a pure reimplementation, not a behavior change. `skip_specs: true` is set accordingly; the existing spec files remain the acceptance criteria for this change and are not modified.

## Impact

- **Removed**: `NinjagoScanner.PictureService/` (C#), `NinjagoScanner.PictureService.Tests/` (C#), their entries in `NinjagoScanner.slnx`.
- **Added**: `picture_service/` (Python, `uv`-managed), its pytest test suite, a new Dockerfile and Fly.io app definition, an updated GitHub Actions workflow.
- **Changed**: `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs` replaced by a hand-written fake `CardPictureServiceBase` implementation; `.vscode/` compound launch config; `CLAUDE.md` Commands section.
- **Unchanged, cross-cutting**: `Protos/picture_service.proto` and `Protos/catalog.proto` remain the shared, language-agnostic contracts. `NinjagoScanner.Web` and `NinjagoScanner.CatalogService` require **no code changes** — both continue speaking gRPC to whatever is listening at PictureService's address.
- **Dependencies**: adds `grpcio`, `grpcio-tools`, `aioboto3`, `langchain-google-genai`, `pytest` (Python); removes the AWS SDK for .NET and `Grpc.AspNetCore` dependencies from the (deleted) C# PictureService project.
- **Explicitly out of scope** (planned as a follow-up change): the multi-step extraction/identification pipeline, LangGraph-based branching by card layout, LangSmith tracing and dataset-based evaluation, and per-attribute accuracy scoring against verified photos. This change only carries the existing single-call Gemini analysis behavior across languages.
