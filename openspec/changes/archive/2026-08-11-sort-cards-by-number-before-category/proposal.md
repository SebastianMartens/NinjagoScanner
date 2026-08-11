## Why

Cards were ordered by series `sort_order`, then category (alphabetically),
then card number. Because category is a printed grouping label rather than a
numbering scheme, and card numbers run contiguously across categories within
a series (e.g. "Heroes" 1-81, "Action Cards" 101-153), an alphabetically-early
category (like "Action Cards") that happens to start well into a series'
number range appears before an alphabetically-later category (like "Heroes")
that starts at card 1. The visible result: a series' default card listing
starts at 101 instead of 1, which reads as wrong regardless of the underlying
reason.

## What Changes

- Drop category from the base card-ordering chain everywhere cards are
  ordered by number without an explicit category grouping. New base order:
  series `sort_order`, then card number (using the existing canonical
  numeric-then-alphanumeric-by-prefix rule), then card name. Category remains
  a real, filterable, displayed field - it's just no longer a sort key ahead
  of card number.
- Wherever cards ARE explicitly grouped by category (Collection's "nach
  Kategorie" grouping and its category filter dropdown; the Gallery page's
  per-category sections and its category filter dropdown), order the
  category groups themselves by each category's lowest card number, not
  alphabetically - so "Heroes" (starts at 1) still appears before "Action
  Cards" (starts at 101), consistent with the new base order.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `catalog-service-card-catalog`: the "Cards are sorted deterministically"
  requirement drops category from the ordering chain (series `sort_order`,
  then card number, then card name).
- `web-collection-list`: the grouping requirement's category-grouping order
  changes from alphabetical to each category's lowest card number; the
  category filter dropdown's option order changes the same way.

## Impact

- `NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`: remove the
  `.ThenBy(card => card.Category, ...)` step from `LoadSnapshot()`'s card
  ordering.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: remove the same step
  from `GetCollectionOverviewAsync` and `GetGalleryCardsAsync`.
- `NinjagoScanner.Web/Components/Pages/Collection.razor`: category-grouping
  and the category filter dropdown switch from alphabetical to
  lowest-card-number ordering.
- `NinjagoScanner.CatalogService.Tests`/`NinjagoScanner.Web.Tests`: update
  tests that assert the old category-then-number order; add coverage for the
  new lowest-card-number category-group ordering.
- The still-unarchived `add-web-gallery-page` change's `web-gallery-page`
  delta spec and `Gallery.razor` are amended directly (not through a
  "Modified Capabilities" delta here, since that capability hasn't been
  archived into `openspec/specs/` yet) to apply the same lowest-card-number
  ordering to Gallery's category sections and its category filter dropdown.
- No proto/gRPC contract changes — ordering behavior only.
