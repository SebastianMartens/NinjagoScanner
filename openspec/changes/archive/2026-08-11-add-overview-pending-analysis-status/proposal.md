## Why

The Overview page shows a total photo count (4872) alongside an analysis-status breakdown of `ok`/`uncertain`/`failed` counts, but those three counts don't sum to the total. Photos that have never been scanned by Gemini have no sidecar file, so `PictureService` reports their analysis status as `"unknown"` (see `PictureScannerGrpcService.ToCardEntry`) — a value `CardCatalogService.BuildAnalysisStatusCounts` doesn't bucket into any of the three shown counts. Those photos are counted in the total but silently dropped from the breakdown, so the numbers don't add up and a person can't tell how many photos still need analysis.

## What Changes

- Add a fourth analysis-status bucket, "not yet analyzed" (`pending`/`unknown` sidecar-less photos), to the catalog-wide statistics on the Overview page.
- `CardCatalogService.BuildAnalysisStatusCounts` counts photos whose analysis status is anything other than `ok`/`uncertain`/`failed` (currently `unknown`, and defensively any other unrecognized value) into this new bucket, so the four counts always sum to `TotalPhotos`.
- `Overview.razor` displays the new count alongside the existing `ok`/`uncertain`/`failed` counts.

## Capabilities

### Modified Capabilities
- `web-overview`: the "Photo analysis-status breakdown" requirement gains a fourth "not yet analyzed" bucket so the shown counts always sum to the total scanned-photo count, instead of only covering `ok`/`uncertain`/`failed`.

## Impact

- `NinjagoScanner.Web/Models/SeriesSummaryItem.cs`: `PhotoAnalysisStatusCounts` gains a `NotAnalyzed` (or similarly named) property.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: `BuildAnalysisStatusCounts` computes the new bucket.
- `NinjagoScanner.Web/Components/Pages/Overview.razor`: statistics row shows the new count.
- No PictureService/CatalogService changes — the `unknown` status value already exists and is only being surfaced, not introduced.
