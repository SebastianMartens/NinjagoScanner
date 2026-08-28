## Context

See proposal.md - Why. The relevant code paths are `GeminiApiService.AnalyzeCardAsync` (per-photo call with internal 429/5xx retry) and `PictureScannerGrpcService.Scan` (the batch loop that calls it for every un-skipped photo and writes each result to the sidecar store via `sidecarCache`).

Today, `CreateFailureResult` is called from three places, and all three produce an indistinguishable `AnalysisStatus = "failed"`:
- `GeminiApiService.cs:33-38` — a non-2xx HTTP response, whether from exhausting retries against 429/5xx or an immediate non-retryable status.
- `GeminiApiService.cs:116-166` (`ParseSuccessResponse`) — a 2xx response whose content couldn't be used (empty/malformed JSON), or the model itself reported failure, or series-name matching escalated to failure.
- `PictureScannerGrpcService.cs:122-135` / `:259-272` — an unhandled exception raised while reading photo bytes or calling `AnalyzeCardAsync`, caught around the per-photo work in both `Scan` and `UploadPhoto`.

`Scan`'s skip-check (`PictureScannerGrpcService.cs:108`) only checks sidecar existence, not status, so a `failed` sidecar is permanently skipped by future runs unless `overwrite_existing_sidecars` reprocesses everything.

## Goals / Non-Goals

**Goals:**
- Distinguish, on the analysis result itself, a transport-level failure (Gemini never evaluated the photo) from a content-level failure (Gemini evaluated it and it was rejected, or the response was unusable).
- Make `Scan` stop the batch immediately on a transport-level failure instead of burning the retry budget of every remaining photo.
- Make `Scan` retry-eligible any sidecar whose status is `failed`, so re-running `Scan` later picks up both never-analyzed photos and previously-failed ones without `overwrite_existing_sidecars`.
- Surface the early-stop condition through `ScanSummary` and the Overview page so a person knows to come back later.

**Non-Goals:**
- Splitting `AnalysisStatus` into more granular values (e.g. a distinct `rate_limited` status). `failed` plus its `ErrorMessage` remains the single status a human sees; the transport/content distinction is only used internally by `Scan` to decide whether to keep going.
- Any automatic/scheduled retry — recovery stays a manual click of the existing "Start Gemini Scan" button.
- Changing `UploadPhoto`'s behavior beyond reusing the same distinction internally where relevant — it already writes a single `failed` sidecar today on exhaustion, and that sidecar is now itself retry-eligible the next time `Scan` runs (see above), which closes the loop without any changes to `UploadPhoto` itself.
- Reworking `AnalyzeCardAsync`'s existing retry count/backoff (3 attempts, `retry_delay_ms * attempt`) — confirmed acceptable as-is.

## Decisions

**Carry the transport/content distinction as a field on `CardAnalysisResult`, not as an exception or a separate return type.**
`Scan` and `UploadPhoto` already consume `CardAnalysisResult` uniformly; adding a field (e.g. `IsTransportFailure`) keeps both call sites simple and keeps the existing try/catch-and-continue shape in `Scan`. Alternative considered: throwing a distinct exception type from `AnalyzeCardAsync` on transport failure. Rejected — it would require `Scan` to special-case control flow around exceptions vs. return values for what is otherwise the same "here's a failed result" shape, and would make the exception-vs-result split inconsistent with how `ParseSuccessResponse` already returns content failures as plain results.

**Treat any exception raised while attempting a photo's analysis (not just HTTP-level exceptions inside `AnalyzeCardAsync`) as a transport-level failure in `Scan`.**
The existing per-photo `try/catch` in `Scan` wraps both `photoStore.GetBytesAsync` and `AnalyzeCardAsync`. Rather than threading the transport/content distinction through two different failure-construction sites (one inside `GeminiApiService` returning a typed result, one inside `PictureScannerGrpcService`'s catch block constructing a result by hand), any exception caught at that level is treated as transport-level: something upstream of a content judgment went wrong, and continuing to grind through the rest of the batch under the same condition (S3 unavailable, network partition, etc.) is just as wasteful as continuing through a 429. This matches the "Gemini/infrastructure never evaluated this photo" framing used throughout.

**`Scan`'s skip-check keys off `AnalysisStatus`, defaulting the retry-eligible set to `{no sidecar, failed}`.**
`ok` and `uncertain` remain "leave it alone unless overwrite is requested" — those are results a human might already be reviewing (and `ReviewStatus` carries forward across a rescan regardless, per the existing "preserves ReviewStatus" requirement). Only `failed` gets automatically retried, since it's the status representing "we don't actually have a usable answer yet."

**`ScanSummary` gains a boolery field for "stopped early," kept independent of `has_configuration_error`.**
The two are different failure modes: `has_configuration_error` means the batch never started (bad API key, unreachable CatalogService, etc.); the new field means the batch started, made partial progress, and stopped mid-way. Overview's status message branches on both independently so the counts (processed/skipped/uncertain/failed) are still shown alongside the early-stop note.

## Risks / Trade-offs

- **[Risk]** A photo whose photo bytes are simply corrupt in storage (unrelated to Gemini) would now also abort the batch, since it's caught by the same generic exception handler → **Mitigation**: acceptable per the "any exception here means infrastructure trouble" framing agreed for this change; a future change can narrow this further if it proves too aggressive in practice.
- **[Risk]** A `failed` photo caused by a genuinely bad picture (blurry, not a card) will be retried on every subsequent `Scan` run indefinitely, spending quota on a photo that will never succeed, until a human manually reviews/replaces/deletes it → **Mitigation**: explicitly accepted trade-off (see proposal.md - Impact, "Out of scope"); a more precise status taxonomy is future work if this becomes a real quota cost.
- **[Risk]** Stopping the batch on the very first transport-level failure (rather than requiring a few in a row) could abort on a one-off blip that would have succeeded on the next photo → **Mitigation**: `AnalyzeCardAsync` already gives each photo 3 internal attempts with growing backoff before surfacing a transport failure, which is itself decent evidence of a sustained condition; confirmed acceptable as the stopping threshold.

## Migration Plan

No data migration. Existing `failed` sidecars written before this change are, from the moment it deploys, automatically retry-eligible on the next `Scan` run — no backfill step needed. Rollback is a plain revert; no schema or stored-data changes to unwind.
