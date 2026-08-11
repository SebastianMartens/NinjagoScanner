## Context

See proposal.md - Why/What Changes for motivation and scope. This section only
covers the existing code the design builds on.

- `CardCatalogService.GetCollectionOverviewAsync()` joins catalog cards with photo
  ownership but only keeps a *count* (`OwnedCopies`) — the matched `CardEntry`
  values are discarded once counted (`BuildOwnershipLookup`), so it cannot supply
  a thumbnail URL.
- `CardCatalogService.GetCollectionCardDetailsAsync(series, cardNumber)` does
  return real photo URLs (`BuildCardPhotos`), but each call re-fetches the entire
  catalog (`ListAllCards`) and the entire unfiltered photo list (`ListCards`, no
  series filter exists in the proto) for a single card — calling it once per card
  to render a series-sized grid would be an N+1 pattern.
- `CatalogRepository.EnumerateCardEntries` (CatalogService) already flattens
  nested category JSON keys into a single label via `" / "` join
  (`BuildCategoryLabel`), so the catalog's `Puzzle_Cards` → `Day_of_the_Departed`
  nesting already arrives at the Web app as one flat string:
  `"Puzzle Cards / Day of the Departed"`. No catalog-service or proto change is
  needed to detect puzzle sub-groups.
- `CardsTable.razor` already implements an in-place image lightbox with pure
  Blazor state (`previewImageUrl`/`OpenImagePreview`/`CloseImagePreview`) and CSS
  classes `table-image-preview-backdrop` / `-dialog` / `-close` / `-caption` — no
  JS interop involved.
- `Collection.razor` already implements client-side grouping (`GroupedCards`,
  `BuildGroups`) over a flat list fetched once, and resets an invalid category
  filter when the series selection changes.

## Goals / Non-Goals

**Goals:**
- Reuse existing ownership-matching, category-labeling, and lightbox patterns
  rather than introducing new mechanisms for the same concerns.
- Keep the new server-side read to the same cost profile as today's Collection
  page load (one catalog fetch + one photo fetch per page load).

**Non-Goals:**
- No change to `CardCatalogService`'s catalog service or picture service gRPC
  contracts (`ListCards` stays unfiltered by series — see Risks).
- No editing/sidecar-update capability on the Gallery page; that remains
  Collection's job.
- No virtualization/infinite-scroll of the image grid — a single series' image
  count is the bound the proposal already relies on to keep this fast.

## Decisions

**New `CardCatalogService.GetGalleryCardsAsync(series)` method, not a reuse of
existing methods.** It performs the same single-pass shape as
`GetCollectionOverviewAsync` (one `ListAllCards` call, one `ListCards` call,
joined via the existing `BuildOwnershipKey`), but instead of collapsing matches
into a count, it keeps the first matched photo's URL — using the same
determinism rule `BuildCardPhotos` already uses (`OrderBy(ImageFileName,
OrdinalIgnoreCase)`, take first), so "which photo shows" is consistent with what
Collection's detail pane would show as `SelectedPhoto` by default. Filters to the
requested series before returning, since the page never needs more than one
series at a time.
*Alternative considered*: extend `GetCollectionOverviewAsync` to also carry an
`ImageUrl`. Rejected — Collection's table has no use for it, and it would compute
a thumbnail URL for all series on every Collection load, not just the one the
Gallery page is showing.

**Server returns a flat, catalog-ordered list; Razor groups into category
sections client-side.** Mirrors `Collection.razor`'s existing `GroupedCards`
pattern instead of inventing a second, server-shaped grouping structure. Cards
are ordered by `SortOrder` then card number (via `CardNumberSorting`) then
card name — category is not a sort key in the base list, per the
[[sort-cards-by-number-before-category]] change, since categories are printed
groupings, not a numbering scheme, and an alphabetically-early category can
start well into a series' number range (e.g. "Action Cards" at 101 vs.
"Heroes" at 1). Category *sections* are therefore ordered by each category's
lowest card number, not alphabetically by label — the same rule
`Collection.razor`'s category grouping and category dropdown use. Cards
*within* a section keep the server's card-number ordering.

**Puzzle-grid detection is a string check on the flattened category label**
(`category.StartsWith("Puzzle Cards", StringComparison.OrdinalIgnoreCase)`) done
in the Razor page, not a new catalog field. This is possible only because
`CatalogRepository` already normalizes `Puzzle_Cards` → `"Puzzle Cards"` and joins
sub-group names with `" / "` (see Context) — the label itself is the signal.
*Alternative considered*: add an `IsPuzzle` bool to the catalog gRPC contract.
Rejected as unnecessary — the existing label already carries this information
losslessly, and prefixing is a one-line check colocated with the one place that
needs it.

**Category sections use explicit `repeat(5, 1fr)` / `repeat(3, 1fr)` CSS Grid**
column counts, not the `auto-fill`/`minmax` pattern used by
`.series-tile-grid`/`.review-photo-grid`. Those existing grids intentionally fit
"as many tiles as reasonably sized" — the spec here requires an *exact* column
count (5, or 3 so a 9-card sub-puzzle forms a literal 3x3), which `auto-fill`
cannot guarantee at arbitrary viewport widths. The existing `max-width: 700px`
breakpoint in `app.css` is extended to collapse both grids to fewer columns on
narrow viewports, same mechanism already used elsewhere in that file.

**Lightbox reuses `CardsTable.razor`'s backdrop/dialog/close/caption pattern**
(new Gallery-scoped CSS classes, same pure-Blazor-state open/close shape) rather
than a JS-interop modal or a third-party lightbox dependency — the existing
pattern already satisfies "in-place, captioned, no navigation."

**Series selector defaults to the first series in catalog sort order** (same
notion of "first" `Overview.razor`/`Collection.razor` already use) rather than
rendering with nothing selected, so the mandatory-selection requirement doesn't
leave first-time visitors looking at an empty page. An optional `?series=` query
parameter is honored on load for deep-linking, matching Collection's existing
`SeriesQueryParameter` pattern — this is a convenience, not a requirement the
spec depends on.

## Risks / Trade-offs

- [Risk] `ListCards` has no series filter, so every Gallery load still fetches
  photo sidecar data for *all* series, not just the selected one → *Mitigation*:
  this is the same cost Collection and Review already pay on every load today;
  no regression is introduced, and adding server-side filtering is out of scope
  for this change.
- [Risk] A category filter value can become stale after switching series (a
  category that existed in the old series may not exist in the new one) →
  *Mitigation*: reuse the same reset-on-series-change logic `Collection.razor`
  already implements for its category filter.
- [Risk] Fixed 5/3-column grids are less flexible than `auto-fill` on unusual
  viewport widths → *Mitigation*: accept the trade-off since the spec requires
  exact column counts (specifically, the literal 3x3 puzzle reassembly); the
  existing mobile breakpoint absorbs the narrow-viewport case the same way it
  already does for other fixed-layout sections of the app.

## Migration Plan

Purely additive: a new route, a new service method, a new nav entry, and new CSS
classes. No existing route, service method, model, or spec's documented
behavior changes, so there is no migration or rollback concern beyond a normal
deploy.
