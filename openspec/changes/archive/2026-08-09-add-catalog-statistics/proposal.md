## Why

The Overview page's per-series tile grid (added by the in-flight `add-series-overview` change) shows progress per series, but gives no catalog-wide picture: how big the whole catalog is, how much of it is owned overall, how many photos have been scanned in total, and how healthy those photos are (analysis outcome, review progress). A person wants that summary without adding up every tile by hand.

## What Changes

- The Overview page ("/") gains a catalog-wide statistics section, shown alongside the existing per-series tile grid: total number of catalog cards, number of those cards with at least one owned/matching photo, and total number of scanned photos.
- The same section shows a breakdown of all scanned photos by analysis status (`ok` / `uncertain` / `failed`) and by review status (`unreviewed` / `verified` / `incorrect`).
- Card-ownership for the catalog-wide count uses the same per-series exact-match rule the series tile grid already uses (trim + case-fold series name match), not `/collection`'s more lenient normalized matching — kept consistent with the existing Overview page convention rather than introducing a third matching rule.
- `CardCatalogService` gains a new aggregation method backing this section; no gRPC contract changes are needed since `AnalysisStatus` and `ReviewStatus` are already exposed on `CardEntry`.

## Capabilities

### Modified Capabilities
- `web-overview`: adds a catalog-wide statistics section (total/owned card counts, total photo count, analysis-status breakdown, review-status breakdown) to the Overview page, in addition to the per-series tile grid already being added by `add-series-overview`.

## Impact

- `NinjagoScanner.Web/Components/Pages/Overview.razor`: new statistics section UI.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: new aggregation method (reusing the per-series ownership matching already used by `GetSeriesSummaryAsync`, plus a catalog-wide pass over all photo entries for analysis/review status counts).
- `NinjagoScanner.Web/Models/`: new result model(s) for the statistics section.
- No changes to `NinjagoScanner.CatalogService` or `NinjagoScanner.PictureService`: all needed data (catalog cards, photo `AnalysisStatus`/`ReviewStatus`) is already exposed via existing gRPC endpoints.
- Depends conceptually on `add-series-overview` (same page, same ownership-matching convention) but does not require it to be archived first — this change's delta spec is additive to `web-overview` and does not restate the series-tile requirements.
