## 1. Gemini analysis result gains a transport/content failure signal

- [x] 1.1 Add a field to `CardAnalysisResult` (`ScannerModels.cs`) marking a failure as transport-level.
- [x] 1.2 In `GeminiApiService.CreateFailureResult`/`AnalyzeCardAsync`, mark the result as transport-level when it's produced from a non-2xx HTTP response (retries exhausted against 429/5xx, or an immediate non-retryable status).
- [x] 1.3 Confirm content-level failures (`ParseSuccessResponse`'s empty/malformed-JSON path, model-reported `failed`, series-name-match escalation) leave the field unset.
- [x] 1.4 Add/adjust `NinjagoScanner.PictureService.Tests` coverage for `GeminiApiService` asserting the field's value for each failure path (429-exhausted, 5xx-exhausted, immediate 4xx, malformed JSON, model-reported failure, series-match escalation).

## 2. Scan: retry-eligible failed sidecars and early abort

- [x] 2.1 Update `Scan`'s skip-check (`PictureScannerGrpcService.cs`) to skip only when an existing sidecar's `AnalysisStatus` is `ok` or `uncertain`; treat `failed` the same as "no sidecar" (analyze regardless of `overwrite_existing_sidecars`).
- [x] 2.2 In `Scan`'s per-photo `try/catch`, treat any caught exception as a transport-level failure (same as an `AnalyzeCardAsync` result marked transport-level).
- [x] 2.3 After writing a photo's sidecar, if the result was a transport-level failure, stop the loop without processing the remaining photo IDs.
- [x] 2.4 Add a field to the `ScanSummary` proto message (`picture_service.proto`) indicating the batch stopped early; regenerate/rebuild so `NinjagoScanner.Web`'s generated client picks it up.
- [x] 2.5 Set that field on the `ScanSummary` returned by `Scan` when the loop stops early; leave it unset when the batch completes normally.
- [x] 2.6 Add/adjust `NinjagoScanner.PictureService.Tests` coverage. Scoped down from the original plan: `Scan`'s skip/retry decision was extracted into `PictureScannerGrpcService.ShouldSkipExistingSidecar` and is directly unit tested (existing `failed`/no-sidecar → retry-eligible, `ok`/`uncertain` → skip unless overwrite). The full loop-level behavior (abort mid-batch on a transport failure, `ScanSummary.StoppedEarly`) is not independently testable in this project today — `Scan` reaches a real `CatalogGrpcClient` gRPC call and a hardcoded Gemini HTTPS endpoint with no fake/injectable seam for either, the same pre-existing gap `PictureScannerGrpcServiceUploadPhotoTests` already works around by only asserting up to that boundary. Covered instead by manual verification (4.3).

## 3. Overview page reflects an early stop

- [x] 3.1 Update `PictureServiceClient`'s scan-response mapping to carry the new `ScanSummary` field through to `NinjagoScanner.Web`'s model.
- [x] 3.2 Update `Overview.razor`'s post-scan status message to include an early-stop indication when present, alongside the existing processed/skipped/uncertain/failed counts. Message-building logic extracted to `ScanStatusMessageFormatter` for testability (no component-testing framework, e.g. bUnit, exists in this project yet).
- [x] 3.3 Add/adjust `NinjagoScanner.Web.Tests` coverage for the status-message text in the early-stop case.

## 4. Verification

- [x] 4.1 `dotnet build NinjagoScanner.slnx`
- [x] 4.2 `dotnet test NinjagoScanner.slnx`
- [ ] 4.3 Manually verify via `run`: trigger a scan against a photo directory, confirm a simulated/observed transport failure stops the batch and the Overview message reflects it, then re-run the scan and confirm the previously-failed photo(s) are retried.
