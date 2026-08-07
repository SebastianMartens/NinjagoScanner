## 1. Catalog data

- [x] 1.1 Add `SortOrder` (int) to each of the 16 `NinjagoScanner.CatalogService/cardInfos/series_*.json` files per the table in design.md (series_1=10, series_2=20, series_3=30, series_4=40, series_5=50, series_5NL=55, series_6=60, series_6NL=65, series_7=70, series_7NL=75, series_8=80, series_8NL=85, series_9=90, series_9NL=95, series_10=100, series_11=110)

## 2. Shared gRPC contract

- [x] 2.1 Add `int32 sort_order` to `SeriesEntry`, `SeriesMetadata`, and `CatalogCardEntry` in `NinjagoScanner.CatalogService/Protos/catalog.proto`
- [x] 2.2 Rebuild all three projects so the generated gRPC code (CatalogService, PictureService's client stubs, Web's client stubs) picks up the new fields

## 3. CatalogService

- [x] 3.1 Parse `SortOrder` in `CatalogRepository.LoadSeriesDetails` into the series detail model, defaulting to `0` when absent
- [x] 3.2 Add `SortOrder` to `SeriesCatalogItem`, `SeriesMetadataItem`, and `CatalogCardItem` in `Catalog/CatalogContracts.cs`
- [x] 3.3 Update `CatalogRepository.BuildSeriesList` to order by `SortOrder` ascending, then series name as tiebreaker (drop `Year` as a sort key)
- [x] 3.4 Update `CatalogRepository.LoadSnapshot`'s flat `Cards` ordering to order by series `SortOrder` ascending, then category, then card number, then card name (drop the series-name string sort)
- [x] 3.5 Update `Services/CardCatalogGrpcService.cs` to map `SortOrder` onto `SeriesEntry`, `SeriesMetadata`, and `CatalogCardEntry` in gRPC responses

## 4. Web

- [x] 4.1 Add `SortOrder` to `Models/CollectionCardItem.cs` and `Models/CollectionCardDetails.cs`
- [x] 4.2 Update `Services/CardCatalogService.cs`: populate `SortOrder` when building `CollectionCardItem`/`CollectionCardDetails` from catalog gRPC responses; order `GetCollectionOverviewAsync`'s cards by `SortOrder` instead of series name; stop `GetKnownSeriesAsync` from re-sorting alphabetically — order its result by `SortOrder` instead
- [x] 4.3 Update `Components/Pages/Collection.razor`: build `availableSeries` by deduplicating on series name while preserving `SortOrder` (e.g. `DistinctBy` name over `(name, sortOrder)` pairs, then `OrderBy(sortOrder)`) instead of `Distinct().OrderBy(name)`; change the QuickGrid "Serie" column's `SortBy` to use `SortOrder`; change `GroupCardsBy`'s group-header ordering to use each group's `SortOrder` when grouping by series
- [x] 4.4 Update `Components/Pages/CardsTable.razor`: order the `knownSeries` dropdown by the order returned from `CardCatalogService.GetKnownSeriesAsync()` (now `SortOrder`-based) instead of relying on alphabetical order; when grouping rows by set/series, look up each group's `SortOrder` by normalized set name against the known series list, and sort groups with no match after all known-series groups (alphabetically among themselves)

## 5. Tests

- [x] 5.1 Update `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/NormalizationAndSortingTests.cs`: extend `GetSnapshot_OrdersCards_BySeriesThenCategoryThenNumberThenName` (or add a new test) to cover a series pair where `SortOrder` and alphabetical series-name order disagree (e.g. a `SortOrder`-100 "Serie 10" appearing after a `SortOrder`-20 "Serie 2"), and drop any assertions relying on `Year` for series ordering
- [x] 5.2 Update `NinjagoScanner.CatalogService.Tests/Services/CardCatalogGrpcServiceTests.cs` (and any other affected gRPC service tests) to assert `sort_order` is populated on `SeriesEntry`, `SeriesMetadata`, and `CatalogCardEntry`, and that `ListSeries`/`ListAllCards` ordering follows `sort_order`
- [x] 5.3 Add/update Web-side tests (if present) covering: `availableSeries` ordering, QuickGrid "Serie" column sort, `GroupCardsBy` series-group ordering, and `CardsTable` set/series grouping with an unmatched set name sorting after known series — no Web test project exists in this repo, so nothing to update
