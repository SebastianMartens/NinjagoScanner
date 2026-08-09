## 1. Aggregation

- [x] 1.1 Extend `SeriesSummaryResult` (`NinjagoScanner.Web/Models/SeriesSummaryItem.cs`) with catalog-wide fields: `TotalCatalogCards`, `OwnedCatalogCards`, `TotalPhotos`, `AnalysisStatusCounts`, `ReviewStatusCounts` (small dictionaries or a fixed-shape record keyed by the known status strings).
- [x] 1.2 Extend `CardCatalogService.GetSeriesSummaryAsync()` to compute the new fields from the same `cardsFromCatalog`/`photoEntries` data already loaded: sum per-series `TotalCards`/`OwnedCards` for the catalog-wide totals, and group `photoEntries` by `AnalysisStatus`/`ReviewStatus` for the breakdowns (over all photos, including those with an unrecognized series).
- [x] 1.3 Ensure counts include zero entries for any analysis/review status with no photos (don't omit a status just because its count is zero).

## 2. UI

- [x] 2.1 Add a catalog-wide statistics section to `Overview.razor`, shown alongside the per-series tile grid, following the app's existing plain stat-row styling (no charting library).
- [x] 2.2 Render total catalog cards, owned catalog cards, and total photos as simple stat numbers.
- [x] 2.3 Render the analysis-status and review-status breakdowns (three counts each) in the same section.

## 3. Verification

- [x] 3.1 Manually verify on a populated `cardFotos`/catalog dataset that the catalog-wide "owned cards" count equals the sum of the per-series tiles' owned-card counts.
- [x] 3.2 Manually verify analysis-status and review-status counts sum to the total photo count.
