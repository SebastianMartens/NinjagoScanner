## Why

A card photo with no sidecar record yet (freshly uploaded, never scanned) should read as "not analyzed" in the UI. Today it doesn't: `PictureScannerGrpcService.ToCardEntry` defaults a missing sidecar's `AnalysisStatus` to the literal `unknown`, but `Review.razor`'s label/filter logic only recognizes `ok`, `uncertain`, and `pending` — so an unanalyzed photo silently falls into the `_ => "Fehler"` (Error) default case and is shown to users as if analysis had failed. Meanwhile the existing `picture-service-card-listing` spec already requires this case to report `pending` — the current code violates its own spec. Separately, `pending` is reused for a second, narrower case (a user edits an attribute — set name, card number, language, review status — on a photo that has no sidecar yet, so a new one is created on the fly) via a raw string literal duplicated across four call sites in `PictureScannerGrpcService.cs`, with no shared constant on the PictureService side at all.

`pending` is a misleading name for both cases: nothing is "pending" or in flight — the photo simply hasn't been analyzed. This change fixes the `unknown` bug and renames the status to `notAnalyzed`, which reads correctly for both the "no sidecar at all" case and the "sidecar exists only because of a manual edit, never analyzed" case.

## What Changes

- Replace `ToCardEntry`'s (`PictureScannerGrpcService.cs`) no-sidecar `unknown` default and the four `UpdateXxx` create-if-missing fallbacks' `pending` literal with a single normalization rule: `AnalysisStatus` reports as `notAnalyzed` whenever the underlying value — missing sidecar, unset field, or anything else — does not case-insensitively match `ok`, `uncertain`, or `failed`. This is a fallback, not a value that has to be written: the four `UpdateXxx` methods stop writing any explicit analysis status when creating a sidecar on the fly, and any already-stored legacy `pending` record reads correctly as `notAnalyzed` automatically, with no data migration or backfill required.
- Add a `NotAnalyzed` constant to PictureService's own internal `AnalysisStatuses` class (`ScannerModels.cs`), which currently only defines `Ok`/`Uncertain`/`Failed`, and use it as the normalization fallback.
- Rename Web's `AnalysisStatuses.Pending` constant to `AnalysisStatuses.NotAnalyzed` (value `notAnalyzed`) and update its usages in `Review.razor`.
- Update the Review page's analysis-status filter option and label so the not-analyzed case reads correctly in the UI (replacing the current "Kein Sidecar" wording, which no longer fits a status that also covers "sidecar exists but was never analyzed").
- Fix `Scan`'s re-analysis skip check (`ShouldSkipExistingSidecar`): it currently skips any existing sidecar whose status isn't `failed` — including a `notAnalyzed` one created by a manual edit — even though its own doc comment says only a completed analysis (`ok`/`uncertain`) should be skipped. A photo whose sidecar exists only because someone corrected its set name/card number/language/review status before it was ever analyzed was therefore never picked up by `Scan`. Fixed to skip only on `ok`/`uncertain`, matching the already-documented `picture-service-photo-scan` spec.

## Capabilities

### Modified Capabilities
- `picture-service-card-listing`: `ListCards` reports `notAnalyzed` whenever a sidecar is missing entirely, or exists but its stored `AnalysisStatus` doesn't recognize as `ok`/`uncertain`/`failed` (covering the current `unknown` bug and any legacy `pending` records) — no migration needed.
- `picture-service-sidecar-editing`: the create-if-missing fallback in `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, and `UpdateReviewStatus` no longer writes an explicit analysis status; the created sidecar reports as `notAnalyzed` via the same normalization until it's actually analyzed.
- `picture-service-photo-scan`: `Scan`'s skip check now retries a `notAnalyzed` sidecar (and any other non-`ok`/`uncertain` status) instead of treating it as already handled.
- `web-card-review-flow`: the analysis-status filter's `Pending` option and label are renamed to reflect `notAnalyzed`.

## Impact

- `NinjagoScanner.PictureService/Services/PictureScannerGrpcService.cs` — `ToCardEntry` gains a shared normalization helper; the four `UpdateXxx` create-if-missing fallbacks drop the explicit status literal; `ShouldSkipExistingSidecar` now checks for `ok`/`uncertain` instead of excluding `failed`.
- `NinjagoScanner.PictureService/ScannerModels.cs` — internal `AnalysisStatuses` class gains `NotAnalyzed`.
- `NinjagoScanner.Web/Models/Statuses.cs` — `AnalysisStatuses.Pending` renamed to `AnalysisStatuses.NotAnalyzed` (value `notAnalyzed`).
- `NinjagoScanner.Web/Components/Pages/Review.razor` — filter option, `MatchesAnalysisStatusFilter` usage, and `GetAnalysisStatusLabel` switch.
- Test projects referencing the `pending`/`unknown` literals or `AnalysisStatuses.Pending`: `NinjagoScanner.PictureService.Tests/Services/PictureScannerGrpcService*Tests.cs`, `NinjagoScanner.Web.Tests/Pages/ReviewFilterTests.cs`.
- `openspec/GLOSSARY.md` — Analysis Status enum values.
- Not touched: `NinjagoScanner.CardFotosMigration` (one-time, already-run tool for the filesystem→S3/DynamoDB migration; its historical `pending` seed value needs no update since it's covered by the same read-side normalization).
- No proto/gRPC contract change: `AnalysisStatus` is already a free-form `string` field, so this is a value convention change, not a schema change.
- No data migration: existing DynamoDB sidecar records are left as-is; any non-`ok`/`uncertain`/`failed` value they contain (including legacy `pending`) is normalized on every read, indefinitely.
