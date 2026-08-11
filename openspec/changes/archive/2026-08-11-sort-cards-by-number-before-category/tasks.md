## 1. CatalogService

- [x] 1.1 `CatalogRepository.LoadSnapshot()`: remove
      `.ThenBy(card => card.Category, StringComparer.OrdinalIgnoreCase)` from
      the `Cards` ordering chain.
- [x] 1.2 Update `NormalizationAndSortingTests.GetSnapshot_OrdersCards_BySortOrderThenCategoryThenNumberThenName`
      to the new expected order (card number now wins over category); rename
      to reflect the new chain.
- [x] 1.3 Add a test with a category that starts at a higher card number than
      another category whose name sorts alphabetically later (mirroring the
      real "Action Cards" (101) vs "Heroes" (1) case), asserting card-number
      order wins.

## 2. Web base ordering

- [x] 2.1 `CardCatalogService.GetCollectionOverviewAsync`: remove the
      `.ThenBy(card => card.Category, ...)` step.
- [x] 2.2 `CardCatalogService.GetGalleryCardsAsync`: remove the same step.
- [x] 2.3 Update/add `CardCatalogService` tests covering the same
      "alphabetically-later category starts at a lower card number" case for
      both methods.

## 3. Collection.razor grouping and dropdown

- [x] 3.1 Add `BuildCategoryGroups(IEnumerable<CollectionCardItem>)`: groups by
      `Category`, ordered by each group's minimum
      `CardNumberSorting.BuildSortKey`, leaving the generic `BuildGroups`
      helper (still used for "ownership" grouping) untouched.
- [x] 3.2 `GroupedCards`'s `"category"` branch: call `BuildCategoryGroups`
      instead of `BuildGroups(FilteredCards, card => card.Category)`.
- [x] 3.3 `AvailableCategories`: change from
      `.OrderBy(category => category, StringComparer.OrdinalIgnoreCase)` to
      grouping by category and ordering by each group's minimum sort key,
      same as 3.1.

## 4. Gallery.razor grouping and dropdown (amends the still-open add-web-gallery-page change)

- [x] 4.1 `GallerySections`: change `.OrderBy(group => group.Key, ...)` to
      order by each group's minimum `CardNumberSorting.BuildSortKey`.
- [x] 4.2 `AvailableCategories`: change from alphabetical to the same
      minimum-sort-key ordering.
- [x] 4.3 Update `openspec/changes/add-web-gallery-page/specs/web-gallery-page/spec.md`'s
      "Optional Category Filter" requirement text/scenarios from
      "ordered alphabetically by category label" to "ordered by each
      category's lowest card number."
- [x] 4.4 Update `openspec/changes/add-web-gallery-page/design.md`'s note
      about category-section ordering to match (currently says "ordered
      alphabetically by label, same as Collection.razor's BuildGroups").

## 5. Verification

- [x] 5.1 Ran `dotnet test`: 92/92 passed (17 PictureService, 56 CatalogService,
      19 Web).
- [x] 5.2 Ran the app locally against Serie 1 (real catalog data, "Action
      Cards" starts at 101, "Good Guys" at 1): Collection's default view now
      starts at card 1 ("Kai"); Collection's category dropdown lists "Good
      Guys", "Villains", "Action Cards", ... (by lowest card number, not
      alphabetically); Gallery's page for Serie 1 starts with the "Good Guys"
      section (card 1) and its category dropdown matches the same order.
