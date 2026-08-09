## 1. Model changes

- [x] 1.1 Add a nullable `CardName` field to `CardReviewGroup` (`NinjagoScanner.Web/Models/CardReviewGroup.cs`), populated only for matched groups (`IsCatchAll == false`).

## 2. Grouping logic

- [x] 2.1 In `CardCatalogService.GetReviewGroupsAsync` (`NinjagoScanner.Web/Services/CardCatalogService.cs`), load the full catalog card list via the existing `LoadCardsFromCatalogServiceAsync` and build a normalized lookup keyed by `(NormalizeSeriesKey(series), NormalizeCardNumber(cardNumber))` → catalog entry (series name, card number, card name, sort order), reusing the normalization already used by the collection-page matching.
- [x] 2.2 Replace the raw `SetName`/`CardNumber` grouping key with a lookup into the normalized catalog dictionary: on a hit, use the catalog entry's canonical `SeriesName`/`CardNumber` as the group key and attach its `CardName`; on a miss, route the photo to the catch-all group (covers blank `SetName`/`CardNumber`, unrecognized series, and a recognized series with an unrecognized card number).
- [x] 2.3 Keep group ordering by the matched catalog entry's `SortOrder`, then `CardNumber`, with the catch-all group sorted last, per the updated spec.
- [x] 2.4 Update `CardReviewGroup.Key` construction (used to preserve position across reloads) to use the normalized/canonical `(SeriesName, CardNumber)` pair so it stays stable across raw-string variants. (Satisfied as a side effect of 2.2: `SeriesName`/`CardNumber` are now always the catalog's canonical values, and `Key` already derives from them.)

## 3. UI changes

- [x] 3.1 Update `GroupTitle` in `Review.razor` to include the resolved `CardName` for matched groups (e.g. `"{SeriesName} · Nr. {CardNumber} · {CardName}"`), leaving the catch-all group's title unchanged (no card name).

## 4. Tests

- [x] 4.1 Add/update tests for `GetReviewGroupsAsync` covering: raw-value grouping (existing behavior), normalization merging spelling/format variants into one group, a recognized series with an unrecognized card number landing in catch-all, and the resolved `CardName` being attached to matched groups only. No test project existed for `NinjagoScanner.Web`; created `NinjagoScanner.Web.Tests` with in-process gRPC test hosts (`Fixtures/CatalogServiceTestHost.cs`, `Fixtures/PictureServiceTestHost.cs`) that run the real `CardCatalogGrpcService`/`PictureScannerGrpcService` over loopback HTTP/2, and `Services/CardCatalogServiceReviewGroupsTests.cs` covering all the above scenarios in one end-to-end test.
- [x] 4.2 Add/update a `Review.razor` (or equivalent component) test asserting the group header shows the catalog card name for a matched group and omits it for the catch-all group. Changed `GroupTitle` from `private` to `internal static` (no other behavior change) so `Components/ReviewGroupTitleTests.cs` can call it directly as a pure unit test, avoiding a bUnit dependency for a single pure function.

## 5. Spec sync

- [x] 5.1 Verify the delta spec at `openspec/changes/review-page-catalog-card-lookup/specs/web-card-review-flow/spec.md` matches the implemented behavior before archiving.
