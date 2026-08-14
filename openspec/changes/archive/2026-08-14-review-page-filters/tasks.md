## 1. Analysis status filter

- [x] 1.1 In `Review.razor`, add an `analysisStatusFilter` field (default `AllFilterValue`) alongside the existing `reviewStatusFilter` field.
- [x] 1.2 Add an `analysis-status` `<select>` control next to the existing "Status-Filter" dropdown (lines 18-26), with an `Alle` option plus `Ok`/`Uncertain`/`Failed`/`Pending`, using the `AnalysisStatuses` constants and an `OnAnalysisStatusFilterChanged` handler that resets `currentIndex = 0` (mirroring `OnReviewStatusFilterChanged` at line 316).
- [x] 1.3 Add a `MatchesAnalysisStatusFilter(CardReviewGroup group)` predicate mirroring `MatchesReviewStatusFilter` (line 231), matching on `photo.AnalysisStatus` (case-insensitive).
- [x] 1.4 Add a label helper for analysis-status option text (mirroring `GetReviewStatusLabel`) if display labels differ from the raw constant values.

## 2. Free-text search filter

- [x] 2.1 Add a `searchText` field (default empty string) to `Review.razor`.
- [x] 2.2 Add a search `<input>` in the `review-nav` header, bound with `@bind="searchText" @bind:event="oninput"`, matching the pattern used in `Collection.razor` and `CardsTable.razor`.
- [x] 2.3 Add a `MatchesSearchFilter(CardReviewGroup group)` predicate: returns true when `searchText` is null/whitespace, otherwise true if any photo in the group has `CardName` or `CardNumber` containing the trimmed search text (`StringComparison.OrdinalIgnoreCase`).
- [x] 2.4 Ensure the search input's `oninput` updates re-render `FilteredGroups` and reset `currentIndex = 0` (e.g. via an `oninput`/`@bind:after` handler), consistent with how the other two filters reset navigation.

## 3. Combine filters

- [x] 3.1 Update `FilteredGroups` (line 229) to require `MatchesReviewStatusFilter(group) && MatchesAnalysisStatusFilter(group) && MatchesSearchFilter(group)`.
- [x] 3.2 Update the standalone `MatchesReviewStatusFilter` usage at line 481 (used when re-evaluating the current group after a status change) to the same combined predicate, so post-action group re-evaluation (e.g. after "Confirm all" or a status change) stays consistent with the active filters.
- [x] 3.3 Verify `currentIndex` reset/clamping logic (lines 244, 313, 377, 438) continues to work correctly against the combined `FilteredGroups`.

## 4. Verification

- [x] 4.1 Run the app (`CatalogService` + `PictureService` + `Web`) and manually verify: analysis-status filter narrows groups correctly; search matches by card number and by card name; all three filters combine with AND; changing any filter resets to the first matching group; clearing all filters restores the full list.
- [x] 4.2 Run `dotnet test NinjagoScanner.Web.Tests` and add/update tests covering the new filters and their combination with the existing review-status filter, following the existing test patterns for `Review.razor`.
