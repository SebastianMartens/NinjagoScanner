## 1. Project scaffolding

- [ ] 1.1 Create `picture_service/` at the repo root as a `uv`-managed project (`pyproject.toml`, `uv.lock`) and verify `uv run python -c "print('ok')"` succeeds
- [ ] 1.2 Add `grpcio`, `grpcio-tools`, `aioboto3`, `langchain-google-genai`, `pytest`, `pytest-asyncio` as dependencies and verify `uv sync` completes cleanly
- [ ] 1.3 Wire proto codegen (a `uv run` script or `Makefile` target invoking `python -m grpc_tools.protoc` against `NinjagoScanner.PictureService/Protos/picture_service.proto` and `catalog.proto`, output not committed) and verify the generated `_pb2.py`/`_pb2_grpc.py` modules import successfully
- [ ] 1.4 Add a `.gitignore` entry for the generated proto stubs

## 2. Config and models

- [ ] 2.1 Port `ScannerConfig.cs`'s layered resolution (per-request override → config key → env var → default) to `config.py` and verify unit tests cover all three resolution paths for each setting
- [ ] 2.2 Port `ScannerModels.cs` (`CardAnalysisResult`, statuses, languages) to `models.py` as dataclasses/pydantic models and verify field names/types match the C# source 1:1
- [ ] 2.3 Port `SidecarTable.cs`'s `SidecarRecord` to `models.py`, preserving exact field names for the DynamoDB mapping in section 4

## 3. Catalog client

- [ ] 3.1 Generate Python stubs for `catalog.proto` and implement `catalog_client.py` porting `CatalogGrpcClient.LoadSeriesCatalogAsync`, and verify it round-trips against the existing `NinjagoScanner.CatalogService` (run it locally, call `ListSeries`)
- [ ] 3.2 Port `SeriesCatalogService.cs` (prompt building, `ResolveSetName` matching logic) to `series_catalog_service.py` and verify unit tests cover the same matching scenarios as `picture-service-series-name-matching/spec.md`

## 4. Storage layer (aioboto3)

- [ ] 4.1 Implement `photo_store.py` (S3, via `aioboto3`) porting `PhotoStore.cs` — same key layout `photos/{collection_id}/{photo_id}`, same pre-signed URL expiry — and verify unit tests against a mocked S3 (e.g. `moto`) cover put/get/exists/delete/list-by-collection
- [ ] 4.2 Implement `sidecar_table.py` (DynamoDB, via `aioboto3`) porting `SidecarTable.cs` — same partition/sort keys (`CollectionId`/`PhotoId`), same PascalCase attribute names, `DetectedText` written as a DynamoDB List (not Set), `ScannedAtUtc` as the same ISO-8601 string format — and verify unit tests against mocked DynamoDB cover get/put/delete/query-by-collection/scan-all
- [ ] 4.3 Round-trip a handful of real sidecar items exported from production DynamoDB through `sidecar_table.py`'s read path and verify every field parses identically to the C# `SidecarTable.FromDocument` output
- [ ] 4.4 Implement `sidecar_cache.py` porting `SidecarCache.cs`'s write-through/read-through semantics (including `WarmFromStoreAsync`'s "don't overwrite already-cached" behavior) and verify unit tests cover cache-hit, cache-miss, and warm-without-overwrite cases per `picture-service-sidecar-cache/spec.md`

## 5. Gemini analysis

- [ ] 5.1 Implement `gemini_service.py` using `langchain-google-genai`'s `ChatGoogleGenerativeAI` with `with_structured_output` against a pydantic schema matching the existing JSON schema, and verify the request sent (image + series-catalog prompt) matches `picture-service-gemini-analysis`'s "Request built for an image" scenario
- [ ] 5.2 Implement the custom retry wrapper (429/5xx-only, `retry_delay_ms * attempt` backoff, immediate failure otherwise) around the LangChain call and verify unit tests cover every retry scenario in `picture-service-gemini-analysis/spec.md` (rate-limited-then-succeeds, server-error-exhausts-attempts, non-retryable-error)
- [ ] 5.3 Implement transport-vs-content failure classification and verify unit tests cover every scenario under "A failure result indicates whether Gemini evaluated the photo"
- [ ] 5.4 Verify confidence clamping, status normalization, and language normalization unit tests match every scenario in the spec (including NaN/infinite confidence, out-of-range values, unrecognized language)

## 6. gRPC service

- [ ] 6.1 Implement `picture_scanner_service.py` implementing the generated `CardPictureServiceServicer`, porting `Services/PictureScannerGrpcService.cs` RPC-by-RPC: `Scan`, `GetPhotoDownloadUrl`, `ListCards`, `GetCardDetails`, `UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `UpdateReviewStatus`, `MigrateSidecars`, `DeletePhoto`
- [ ] 6.2 Implement the client-streaming `UploadPhoto` RPC (metadata message followed by byte chunks) and verify it against a Python gRPC test client sending a multi-chunk stream
- [ ] 6.3 Implement collection-scoping validation (reject/ignore access to a `collection_id` the service has no record of) and verify unit tests cover `collection-scoped-picture-access/spec.md`'s scenarios, including the `MigrateSidecars` cross-collection exception
- [ ] 6.4 Wire up `main.py`: `grpc.aio` server bootstrap, DI-equivalent construction (config → stores → cache → service), and health/liveness endpoint equivalent to the current dual-port Kestrel setup — verify by starting the service locally and calling each RPC with `grpcurl`

## 7. Observability

- [ ] 7.1 Add OpenTelemetry instrumentation (`opentelemetry-instrumentation-grpc`, OTLP exporter) mirroring the existing service name (`ninjago-scanner-picture-service`) and verify traces arrive in Grafana Cloud
- [ ] 7.2 Explicitly verify the Python OTLP exporter's default protocol against Grafana Cloud's gateway (do not assume the .NET `HttpProtobuf`-override requirement carries over) and set it explicitly if needed

## 8. Python test suite

- [ ] 8.1 Port `NinjagoScanner.PictureService.Tests`' fakes (`FakePhotoStore`, `FakeSidecarStore`) to pytest fixtures and verify the full RPC-level test suite (upload, scan-skip-check, delete, get-card-details, get-photo-download-url, sidecar-cache-consistency, update-card-language, update-card-number, update-review-status) passes
- [ ] 8.2 Add a pytest suite that walks every scenario in every `picture-service-*` and `collection-scoped-picture-access` spec file against the real implementation (not the C# fake), so this suite is the source-of-truth check that catches drift the hand-written C# fake (section 9) can't

## 9. Web.Tests decoupling

- [ ] 9.1 Replace `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs`'s real `PictureScannerGrpcService` with a hand-written fake implementing the generated `CardPictureServiceBase`, backed by the same in-memory `InMemoryPhotoStore`/`InMemorySidecarStore` already in that file, and verify `dotnet test NinjagoScanner.Web.Tests` passes unchanged
- [ ] 9.2 Verify `NinjagoScanner.Web.Tests` no longer references any type from `NinjagoScanner.PictureService` (only the generated proto client/server types)

## 10. Deployment

- [ ] 10.1 Write a Dockerfile for `picture_service/` and verify `docker build` produces a runnable image
- [ ] 10.2 Create the Fly.io app definition for the Python service (reusing the existing `picture-service` app name/`.internal` DNS so `NinjagoScanner.Web`'s configured address is unchanged) and verify `fly deploy` succeeds against a staging/scratch environment
- [ ] 10.3 Rewrite `.github/workflows/deploy-picture-service.yml` for a Python build/test/publish pipeline and verify the workflow runs green on a test branch

## 11. Local dev

- [ ] 11.1 Add a Python launch entry to the `.vscode/` compound launch config (`Launch All (CatalogService + PictureService + Web)`) alongside the two remaining .NET debug configs, and verify starting the compound config brings up all three services

## 12. Cutover

- [ ] 12.1 Deploy the Python service to the existing `picture-service` Fly app during a low-traffic window (see design.md Migration Plan) and verify each RPC once against production storage (a sample `Scan`, an `UploadPhoto`, `ListCards`, a review-status update)
- [ ] 12.2 Monitor OTel/Grafana Cloud traces for errors through a bake-in period before proceeding
- [ ] 12.3 Remove `NinjagoScanner.PictureService/` and `NinjagoScanner.PictureService.Tests/` and their entries in `NinjagoScanner.slnx`, as a separate commit after the bake-in period, and verify `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` still succeed with only the two remaining .NET services

## 13. Documentation

- [ ] 13.1 Update CLAUDE.md's Commands section to document the separate `uv run`/pytest invocation for `picture_service/` alongside the `dotnet build`/`dotnet test` commands for the remaining services
- [ ] 13.2 Update CLAUDE.md's Architecture section to describe PictureService as a Python/`grpc.aio` service and note the gRPC contract as the sole cross-language coupling
