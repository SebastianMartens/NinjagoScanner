## Why

Card-number ordering is currently hardcoded to two known alphanumeric prefixes
(`LE`, `XXL`); anything else falls into an undifferentiated catch-all group
instead of sorting alongside them by its own prefix. The rule should generalize
to any alphanumeric card number, and the same rule should apply consistently
everywhere cards are ordered by number, not just in the one place it's
currently spec'd.

## What Changes

- Generalize card-number sort ordering to: purely numeric card numbers first
  (ascending numeric order), then alphanumeric card numbers (an alphabetic
  prefix followed by a number, e.g. `LE4`, `XXL1`, or any other prefix)
  ordered by prefix alphabetically and then by their numeric suffix, with any
  remaining non-conforming format sorted last by raw text.
- Apply this single rule in every place cards are ordered by card number:
  - `CatalogService`'s `ListAllCards` response ordering (`CatalogRepository`).
  - `CardCatalogService`'s Collection overview ordering and the new Gallery
    page's card ordering (both already delegate to a card-number sort key).
  - Collection's clickable "Nr." column manual sort (currently extracts any
    digit run anywhere in the string, not prefix-aware, and doesn't group
    numeric-before-alphanumeric).
  - CardsTable's row grouping order (currently a plain lexicographic string
    compare with no numeric awareness at all, e.g. `"10"` sorts before
    `"2"`).
  - Review's group ordering (`GetReviewGroupsAsync`, found after the first
    implementation pass): also a plain lexicographic string compare with no
    numeric awareness.
- **BREAKING** (behavior, not API): a card number like `"OTHER1"` now sorts
  within the alphanumeric group by its `OTHER` prefix instead of being dumped
  into the trailing catch-all group; any real prefixes beyond `LE`/`XXL`
  present in catalog or sidecar data will reorder accordingly.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `catalog-service-card-catalog`: generalizes the "Cards are sorted
  deterministically" requirement's card-number ordering rule from
  hardcoded `LE`-then-`XXL`-then-other to any alphabetic prefix, sorted
  alphabetically among themselves.
- `web-card-table-view`: generalizes the "Rows can be grouped" requirement's
  card-number tie-break to the same rule (previously undocumented beyond "by
  card number").
- `web-collection-list`: adds a requirement documenting the "Nr." column's
  manual-sort ordering, which previously had no documented ordering semantics
  beyond "sortable."
- `web-card-review-flow`: generalizes the "Groups are ordered by known series
  order, then card number" requirement's card-number tie-break to the same
  rule (previously a plain string comparison with no numeric awareness).

## Impact

- `NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`: generalize the
  private `ToSortKey` method.
- `NinjagoScanner.Web/Services/CardNumberSorting.cs` (new): shared internal
  helper implementing the canonical rule, used by every Web-side sort site so
  the three Web call sites can't drift out of sync with each other.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: replace its private
  `ToSortKey` with calls to the new shared helper.
- `NinjagoScanner.Web/Components/Pages/Collection.razor`: replace
  `BuildCardNumberSortKey`/`TryExtractNumber` with the shared helper.
- `NinjagoScanner.Web/Components/Pages/CardsTable.razor`: replace the plain
  string `ThenBy(card => card.CardNumber, ...)` tie-break with the shared
  helper.
- `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/NormalizationAndSortingTests.cs`:
  update the existing hardcoded-order test to the new expected order and add
  coverage for an arbitrary third prefix.
- No proto/gRPC contract changes — this is ordering behavior only.

### Addendum (found after first implementation pass)
A grep across the Web project for remaining plain-string `CardNumber` ordering
surfaced one more call site that the original audit missed:
`CardCatalogService.GetReviewGroupsAsync` (feeds the Review page), which
ordered groups with `.ThenBy(group => group.CardNumber,
StringComparer.OrdinalIgnoreCase)` — the same no-numeric-awareness bug class
as the already-fixed `CardsTable.razor` call site. This is covered by tasks
5.1-5.2 and the `web-card-review-flow` spec delta above.
