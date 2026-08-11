## 1. CatalogService

- [x] 1.1 Generalize `CatalogRepository.ToSortKey` from hardcoded `LE`/`XXL`
      checks to the canonical prefix+number regex algorithm (numeric group,
      then alphanumeric group keyed by prefix + zero-padded number, then
      fallback group).
- [x] 1.2 Update `NormalizationAndSortingTests.GetSnapshot_OrdersCards_NumericFirst_ThenLE_ThenXXL_ThenOther_ByCardNumberOrName`
      to the new expected order (`OTHER1` now sorts into the alphanumeric
      group, between `LE3` and `XXL1`); rename it to reflect the generalized
      rule.
- [x] 1.3 Add a test case with a fourth, previously-unseen prefix (e.g.
      `AB1`) interleaved with `LE`/`XXL`/numeric card numbers, asserting it
      sorts into the alphanumeric group by its own prefix rather than a
      catch-all.

## 2. Web shared sort helper

- [x] 2.1 Add `NinjagoScanner.Web/Services/CardNumberSorting.cs`: internal
      static class with `BuildSortKey(string? cardNumber)` implementing the
      canonical algorithm, normalizing its own input (uppercase, strip
      non-alphanumeric characters) so it's safe to call on unnormalized
      sidecar values.
- [x] 2.2 Add unit tests for `CardNumberSorting.BuildSortKey` covering:
      numeric-before-alphanumeric ordering, alphabetical prefix ordering
      across 3+ distinct prefixes, numeric-suffix ordering within the same
      prefix, unnormalized input (lowercase, stray punctuation, leading
      zeros), and the fallback group for non-conforming values.

## 3. Web call sites

- [x] 3.1 `CardCatalogService.cs`: remove the private `ToSortKey` method;
      change the `ThenBy` in `GetCollectionOverviewAsync` and
      `GetGalleryCardsAsync` to call `CardNumberSorting.BuildSortKey`.
- [x] 3.2 `Collection.razor`: remove `BuildCardNumberSortKey`/
      `TryExtractNumber`; change the "Nr." column's `SortBy` to use
      `CardNumberSorting.BuildSortKey`.
- [x] 3.3 `CardsTable.razor`: change the row-grouping `ThenBy(card =>
      card.CardNumber ?? string.Empty, StringComparer.OrdinalIgnoreCase)` to
      `ThenBy(card => CardNumberSorting.BuildSortKey(card.CardNumber),
      StringComparer.Ordinal)`.

## 4. Verification (first pass)

- [x] 4.1 Run `dotnet test` and confirm all tests pass, including the
      updated/added CatalogService and Web tests.
- [x] 4.2 Ran the three services locally and fetched `/gallery?series=Serie 11`
      (24 `LE` cards, 5 `XXL` cards), `/table`, and `/collection`: all return
      200, and `LE1..LE24`/`XXL1..XXL5` still render in ascending numeric-suffix
      order — no regression. Note: the real catalog data never places numeric
      and alphanumeric card numbers (or two different alphanumeric prefixes)
      in the same category/group, so the actual "numeric before alphanumeric,
      arbitrary prefixes sorted alphabetically" behavior change isn't
      observable against real data — it's covered by the new synthetic-data
      unit/integration tests instead (tasks 1.2, 1.3, 2.2).

## 5. Web call sites (missed in first pass)

- [x] 5.1 `CardCatalogService.cs`'s `GetReviewGroupsAsync` (feeds Review):
      change `.ThenBy(group => group.CardNumber, StringComparer.OrdinalIgnoreCase)`
      to `.ThenBy(group => CardNumberSorting.BuildSortKey(group.CardNumber),
      StringComparer.Ordinal)`.
- [x] 5.2 Add/extend a `CardCatalogServiceReviewGroupsTests` case with a mix of
      numeric and alphanumeric card numbers across groups in the same series,
      asserting groups appear numeric-first then alphanumeric-by-prefix.

## 6. Verification (second pass)

- [x] 6.1 Ran `dotnet test`: 89/89 passed (17 PictureService, 55 CatalogService,
      17 Web, including the new `GetReviewGroupsAsync` ordering test).
