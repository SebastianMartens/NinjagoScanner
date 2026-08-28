## Why

When the Gemini API rate-limits (`429`) or errors (`5xx`) for a sustained period, `Scan` keeps grinding through every remaining photo in the batch — each one burning its full retry budget before giving up — and writes every one of them as a permanent `failed` sidecar. Because `Scan`'s skip-check only looks at whether a sidecar exists (not its status), those `failed` sidecars are never retried on a later run unless `overwrite_existing_sidecars` reprocesses the *entire* batch, quota exhaustion and all. A single rate-limit event during a large backfill effectively poisons every photo after it.

## What Changes

- `GeminiApiService.AnalyzeCardAsync`'s result now signals whether a failure was **transport-level** (the Gemini API never actually evaluated the photo — HTTP 429/5xx after exhausting retries, an immediate non-retryable HTTP error, or an exception raised while calling the API) as opposed to **content-level** (Gemini returned a successful response but the photo/analysis itself was rejected — unparseable JSON, model-reported failure, unresolved series).
- `Scan` stops processing the batch immediately after a transport-level failure instead of continuing through the remaining photos. Content-level failures behave as today: counted as `failed`, batch continues.
- `Scan`'s skip-check now retries any existing sidecar whose `AnalysisStatus` is `failed`, not just photos with no sidecar at all — so a later `Scan` run (without `overwrite_existing_sidecars`) naturally picks up where an aborted run left off, alongside any never-analyzed photos.
- `ScanSummary` gains an indicator that the batch stopped early due to repeated Gemini failures, and the Overview page's scan-result message reflects it, so a person knows to come back and click "Start Gemini Scan" again later.
- Retries stay manual — no scheduled/automatic re-trigger is introduced.

## Capabilities

### Modified Capabilities
- `picture-service-gemini-analysis`: the analysis result gains a transport-vs-content failure signal so callers can react differently to "Gemini never looked at this photo" versus "Gemini looked and rejected it".
- `picture-service-photo-scan`: `Scan` retries existing `failed` sidecars (not just missing ones) and aborts the remainder of the batch on a transport-level failure instead of continuing through every photo.
- `web-overview`: the scan-result summary message shown after a manual scan reflects an early stop due to repeated Gemini failures.

## Impact

- `NinjagoScanner.PictureService/ScannerModels.cs`: `CardAnalysisResult` gains a field marking a failure as transport-level.
- `NinjagoScanner.PictureService/GeminiApiService.cs`: sets that field for HTTP-level failures/exceptions; leaves it unset for content-level (2xx-response) failures.
- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs`: `Scan`'s skip-check and loop-continuation logic change; the unexpected-exception catch around the per-photo analysis call is also treated as transport-level.
- `NinjagoScanner.PictureService/Protos/picture_service.proto` (and the generated stubs consumed by `NinjagoScanner.Web`): `ScanSummary` gains a field indicating the batch stopped early.
- `NinjagoScanner.Web/Components/Pages/Overview.razor`: the post-scan status message surfaces the early-stop indicator.
- Out of scope: splitting `AnalysisStatus` itself into more precise values (e.g. separating "quota exhausted" from "unreadable photo" as distinct statuses a human sees) — the existing `failed` status plus its `ErrorMessage` already carries that detail for Review; automatic/scheduled retries.
