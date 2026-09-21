## Context

The Gallery (`Gallery.razor`) renders one tile per catalog card; a tile with a photo is a button that opens a lightbox, and non-puzzle tiles plus the puzzle lightbox carry a "Fehlerhaft" button that calls `PictureServiceClient.UpdateReviewStatusAsync` and reloads the series. The Review page (`Review.razor`) is backed by `ReviewSession`, which holds all photos, the derived `CardReviewGroup`s, the three filters (review status defaults to `Unreviewed`) and a `currentIndex` into the filtered groups. Groups are keyed by the catalog card's series name + card number. See proposal.md for motivation.

The Gallery already knows each tile's catalog series and card number (`GalleryCardItem.Series`, `.CardNumber`), which are exactly the values a group is built from.

## Goals / Non-Goals

**Goals:**
- A photo tile click lands on the Review page showing that card's group.
- Remove the Gallery's Fehlerhaft controls and the lightbox with their now-dead code and CSS.

**Non-Goals:**
- No change to how the Review page groups, filters, or saves photos.
- No new lightbox/zoom replacement on the Gallery.
- No change to the read-only flagged indicator on non-puzzle tiles.
- No deep link to a single photo (the group is the unit of the Review page).

## Decisions

**1. Deep link via query string: `/review?series=<name>&card=<number>`.**
Matches how the Gallery and Collection already take `?series=` (`[SupplyParameterFromQuery]`), needs no route change, and is bookmarkable. The tile becomes a plain `<a href>` (URL-escaped values) rather than a button with an `@onclick` calling `NavigationManager`: works with middle-click/open-in-new-tab, no handler needed. Alternative considered: a route like `/review/{series}/{card}` — rejected, series names contain spaces and card numbers can contain arbitrary characters, so query parameters are simpler and safer.

**2. Positioning lives in `ReviewSession`, not in the razor page.**
Add `bool TryShowCard(string seriesName, string cardNumber)`: finds the group whose normalized ownership key equals that of the given values; if found, sets review-status filter and analysis-status filter to `All`, clears the search text, and sets `currentIndex` to the group's index in `FilteredGroups`; otherwise changes nothing and returns `false`. `Review.razor` calls it once from `OnInitializedAsync` after building the session, when both query parameters are non-blank. This keeps the rule unit-testable in `ReviewSessionTests` (the session is plain C# by design).

**3. Filters are reset to `All` for a deep link.**
The default `Unreviewed` filter would hide a fully verified group, making the link land on the wrong card or on the empty state. Resetting to `All` also makes "next/previous" walk the whole series in card order, which suits browsing from the Gallery. Alternative: keep the `Unreviewed` filter unless the target group is hidden — rejected as surprising (filters would sometimes change and sometimes not).

**4. Reuse the existing key normalization.**
`CollectionQueryService.BuildOwnershipKey` (currently `private static`) becomes `internal static` and `TryShowCard` compares keys built by it, so a link matches exactly what the grouping matches (case/whitespace/leading zeros) instead of comparing `CardReviewGroup.Key` strings raw. The catch-all group has no series/number and is never a target.

**5. Query parameters are read once on initialization.**
The page is `prerender: false` and not designed for re-navigating within the same component instance; a `Gallery` → `Review` navigation always creates a fresh instance. If the user is already on `/review` and follows another deep link, Blazor re-uses the component, but the Gallery is the only source of these links, so that case does not arise. `OnInitializedAsync`-only handling is enough.

**6. Gallery cleanup.**
Remove from `Gallery.razor`: the lightbox markup and state (`lightboxCard`, `OpenLightbox`, `CloseLightbox`), the Fehlerhaft buttons, `MarkFehlerhaftAsync`/`IsMarkingFehlerhaft`/`markingFehlerhaftPhotoId`, `ReloadSeriesCardsAsync`, and the now-unused `PictureServiceClient` injection. Keep `IsFehlerhaft` (drives the read-only flag badge/class on non-puzzle tiles) and `IsPuzzleCategory` (still used by tests and the grid). Remove `.gallery-lightbox-*`, `.gallery-tile-fehlerhaft-btn`, and the `.gallery-tile-media-button` button-reset styles that are no longer needed once the tile media is an anchor (restyle the anchor to match). `GalleryCardItem.PhotoId` stays (harmless, still produced and tested by `CollectionQueryServiceGalleryTests`).

**7. Flagged indicator stays on non-puzzle tiles.**
It is read-only, cheap, and still informative now that flagging happens on another page. The puzzle lightbox indicator disappears together with the lightbox; puzzle tiles never showed one on the grid.

## Risks / Trade-offs

- [The lightbox's quick full-size peek is gone; the Review page is heavier than an overlay] → Accepted per the request; the Review page shows the photo plus all other photos of that card, which is the point.
- [A stale link (group vanished meanwhile) lands on the default view] → `TryShowCard` returns `false` and the page behaves as a normal visit; specified in the delta spec.
- [Returning to the Gallery loses the selected series/category] → Existing behavior of browser Back plus the `?series=` query parameter covers the series; category selection was never in the URL. Not addressed here.
- [Clicking a tile is now a full navigation from a Blazor Server circuit] → Anchors to an interactive page in the same app use Blazor's enhanced navigation; no extra load beyond the Review page's normal initial load.
