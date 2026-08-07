## Why

Series names are strings like "Serie 10" and "Serie 2", so every place that orders series or cards by series name alphabetically (case-insensitive, ordinal) puts "Serie 10" before "Serie 2". `Year` only fixes this by coincidence and breaks down entirely for same-year variants (each "Next Level" series shares its base series' year), so it cannot be relied on as a display-order key. The catalog needs an explicit, manually curated ordering value that is independent of both the series name string and its release year.

## What Changes

- Add a new manually curated `SortOrder` (integer) field to each series' detail JSON (`cardInfos/series_*.json`), using a gapped scheme (10, 20, 30…) with "Next Level" variants placed immediately after their base series (e.g. Serie 5 = 50, Serie 5 Next Level = 55, Serie 6 = 60).
- Add `sort_order` to the `SeriesEntry`, `SeriesMetadata`, and `CatalogCardEntry` gRPC messages in `catalog.proto` (the latter currently carries no series-level fields at all).
- `ListSeries` and the flattened `ListAllCards` card list (CatalogService) order by `SortOrder` instead of series-name string / year.
- The Web app's collection overview (series filter dropdown, series-grouped view, series column sort) and the card table view (set/series-grouped view) order by the catalog's `SortOrder` instead of alphabetically.
- The card table view groups cards by scanned `SetName`, which is freeform text that may not match a known catalog series; unmatched/unknown set names SHALL sort after all known series, ordered alphabetically among themselves.
- `Year` remains in the data and continues to be returned/displayed for informational purposes; it is no longer used to determine order anywhere.
- No validation is added for missing or duplicate `SortOrder` values — the catalog is maintained manually and trusted to be correct.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `catalog-service-card-catalog`: `ListAllCards` ordering changes from series-name-alphabetical to `SortOrder`-based.
- `catalog-service-series-catalog`: `ListSeries` ordering changes from year-then-name to `SortOrder`-based; series entries built from detail data now also source `sort_order`.
- `catalog-service-series-metadata`: `GetSeriesMetadata` response gains a `sort_order` field alongside the existing (now purely informational) `year`.
- `web-collection-overview`: the series filter dropdown and series-grouped view order by the catalog's `SortOrder` instead of alphabetically.
- `web-card-table-view`: the set/series-grouped view orders known series by the catalog's `SortOrder` instead of alphabetically, with unmatched set names sorted after all known series.

## Impact

- **Data**: all 16 `NinjagoScanner.CatalogService/cardInfos/series_*.json` files gain a `SortOrder` field (manually assigned).
- **Shared contract**: `NinjagoScanner.CatalogService/Protos/catalog.proto` changes (`SeriesEntry`, `SeriesMetadata`, `CatalogCardEntry` gain `sort_order`) regenerate gRPC code consumed by both `NinjagoScanner.PictureService` (via `CatalogGrpcClient.cs`) and `NinjagoScanner.Web` (via `CardCatalogService.cs`), even though PictureService doesn't use the new field for anything today.
- **CatalogService**: `Catalog/CatalogContracts.cs`, `Catalog/CatalogRepository.cs` (parsing + all `OrderBy` sites), `Services/CardCatalogGrpcService.cs` (proto mapping).
- **Web**: `Models/CollectionCardItem.cs`, `Models/CollectionCardDetails.cs`, `Services/CardCatalogService.cs` (stop re-sorting `GetKnownSeriesAsync` alphabetically; sort collection cards by `SortOrder`), `Components/Pages/Collection.razor` (availableSeries, QuickGrid "Serie" column, `GroupCardsBy`), `Components/Pages/CardsTable.razor` (known-series dropdown, set/series grouping with catalog lookup + fallback for unmatched names).
- **Tests**: `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/NormalizationAndSortingTests.cs` and any gRPC service tests asserting the old alphabetical/year ordering need updating.
