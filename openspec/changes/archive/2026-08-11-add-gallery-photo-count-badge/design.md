## Context

See proposal.md - Why. `CardCatalogService.GetGalleryCardsAsync` already
builds a `photosByKey` lookup (`ILookup<string, ...>`) keyed by normalized
series+card-number and takes only the first matched photo's filename for
`GalleryCardItem.ImageUrl`. The count of matches in that same lookup is the
`OwnedCopies` value already surfaced elsewhere for the Collection page
(`CollectionCardItem.OwnedCopies`, `openspec/specs/web-collection-list`).

## Goals / Non-Goals

**Goals:**
- Surface the existing photo-match count on the Gallery grid without a new
  data source or gRPC round trip.
- Keep puzzle sub-group tiles visually unchanged (see
  `web-gallery-page` "Puzzle Tiles Show Only the Photo or Placeholder
  Graphic").

**Non-Goals:**
- Changing what counts as a "matched" photo, or how a card's displayed photo
  is chosen among duplicates (unchanged: first by filename, ordinal).
- Adding the badge to the Collection page's table view — it already has an
  `OwnedCopies` column.
- Letting the user open/browse the other matched photos from the badge
  (out of scope; the lightbox still shows only the one displayed photo).

## Decisions

- **Compute `PhotoCount` alongside the existing `matchedPhoto` lookup in
  `GetGalleryCardsAsync`**, using `photosByKey[ownershipKey].Count()`
  instead of introducing a second query. Rationale: the lookup is already
  built and already grouped by the same key; reading `.Count()` off it is
  free relative to the existing `.FirstOrDefault()` call.
- **Add `PhotoCount` (int) to `GalleryCardItem`** rather than reusing the
  `CollectionCardItem.OwnedCopies` name, because the two models are
  independent view models for different pages and `GalleryCardItem` doesn't
  currently share a base type with `CollectionCardItem`. The spec still
  refers to the concept as `OwnedCopies` since it's the same domain count.
- **Badge shown only on photo tiles, not placeholders.** A placeholder tile
  already communicates zero owned copies; a "0" badge on every unowned tile
  (typically the majority of a series' grid) would add visual noise without
  new information.
- **Badge omitted on puzzle sub-group tiles.** The existing spec requires
  puzzle tiles to show "only the photo or the placeholder graphic — no
  caption element of any kind". Adding the badge there would need a spec
  change to that requirement; keeping puzzle tiles untouched avoids
  reopening that behavior for a feature request that was about the general
  grid.
- **Pure CSS overlay, no new Blazor component.** The badge is a `<span>`
  absolutely positioned within the existing `.gallery-tile-photo` tile
  (which is already `position: relative` via `.gallery-tile`), styled in
  `wwwroot/app.css` next to the other `.gallery-tile-*` rules.

## Risks / Trade-offs

- [Badge text overlaps small thumbnail images at very small viewport
  widths] → Use a fixed small font size and padding, and confirm visually in
  the browser at the Gallery page's smallest supported tile size (5
  columns) during implementation.

## Migration Plan

None. Purely additive UI change; `GalleryCardItem.PhotoCount` defaults to 0
for any caller that doesn't set it (there are none besides
`GetGalleryCardsAsync`). No feature flag needed.
