## Context

See proposal.md - Why/What Changes for motivation. This builds directly on
[[generalize-card-number-sort-order]]'s `CardNumberSorting.BuildSortKey` /
`CatalogRepository.ToSortKey`, which already give a correct total order for a
single card number. This change is about a different axis: what precedence
`Category` has relative to `CardNumber` in the overall ordering chain.

Today, three places chain `.OrderBy(SortOrder).ThenBy(Category).ThenBy(CardNumber)...`:
`CatalogRepository.LoadSnapshot()`, `CardCatalogService.GetCollectionOverviewAsync`,
and `CardCatalogService.GetGalleryCardsAsync`. Two places additionally use
`Category` as an explicit UI grouping key with its own separate ordering:
`Collection.razor`'s `BuildGroups` (alphabetical, shared by both "category" and
"ownership" grouping modes) and its `AvailableCategories` dropdown
(alphabetical); `Gallery.razor`'s `GallerySections` and `AvailableCategories`
(both alphabetical, from the `add-web-gallery-page` change, not yet archived).

## Goals / Non-Goals

**Goals:**
- Card number becomes the ordering precedence within a series everywhere
  cards aren't explicitly grouped by category.
- Where category IS the explicit grouping key (Collection's grouped view and
  its dropdown, Gallery's sections and its dropdown), order those groups
  consistently by lowest card number instead of alphabetically, so grouped
  views don't reintroduce the same surprise in a different shape.

**Non-Goals:**
- No change to `CardNumberSorting`/`ToSortKey` themselves (already correct
  per the prior change).
- No change to Collection's "ownership" grouping mode's order (still
  alphabetical via the existing generic `BuildGroups` helper) - it groups by
  a semantic label ("Fehlend"/"Einmal vorhanden"/"Mehrfach vorhanden"), not by
  category, and wasn't reported as wrong.

## Decisions

**Drop `Category` from the three base `OrderBy` chains entirely**, rather than
reordering it after `CardNumber`. Category stays a real field on every
returned card - just not a sort key - since nothing asked for
"category-second" ordering, only "not category-first."

**Category-group ordering is computed explicitly as each group's minimum
card-number sort key**, not derived by relying on `GroupBy`'s incidental
first-occurrence-order behavior over an already-number-sorted source
sequence. Explicit is preferred because it doesn't silently depend on the
caller happening to pass an already-correctly-ordered sequence - a future
change to how `allCards`/`seriesCards` is built wouldn't silently break group
ordering the way an implicit dependency would.

```csharp
.GroupBy(card => card.Category, StringComparer.OrdinalIgnoreCase)
.OrderBy(group => group.Min(card => CardNumberSorting.BuildSortKey(card.CardNumber)), StringComparer.Ordinal)
```

Applied to: `Collection.razor`'s category-groupBy branch (a new
`BuildCategoryGroups`, leaving the generic `BuildGroups` - and its alphabetical
order - untouched for the "ownership" mode) and its `AvailableCategories`;
`Gallery.razor`'s `GallerySections` and its `AvailableCategories`.

**The still-open `add-web-gallery-page` change is amended in place**, not
given a "Modified Capabilities" delta here. `web-gallery-page` hasn't been
archived into `openspec/specs/` yet, so there's no base spec to diff against -
the correct move is editing that change's own delta spec directly, the same
way its "alphabetical vs. catalog order" wording was corrected earlier in that
same change.

## Risks / Trade-offs

- [Risk] Any place relying on the old category-then-number order for display
  (none currently spec'd beyond what's listed above) would silently reorder →
  *Mitigation*: grep confirmed only the three `OrderBy` chains and two
  UI-grouping call sites reference `Category` in an ordering context.
- [Risk] `Collection.razor`'s generic `BuildGroups` helper is reused by both
  "category" and "ownership" grouping; changing its shared alphabetical
  behavior would have altered ownership-group order too → *Mitigation*: kept
  `BuildGroups` untouched and added a separate `BuildCategoryGroups` instead
  of parameterizing `BuildGroups` with a custom comparer, to keep the
  ownership-grouping path's behavior provably unchanged.
