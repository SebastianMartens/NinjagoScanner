## Why

The Gallery page's standard (non-puzzle) category grid is hard-coded to exactly
5 tiles per row (`grid-template-columns: repeat(5, 1fr)`), regardless of how
much horizontal space the browser actually has. Because the grid uses `1fr`
tracks rather than a fixed tile size, zooming out with Ctrl+Mouse Wheel just
shrinks the 5 tiles proportionally instead of revealing more of them — the
user gets a smaller row of 5, not a denser grid. Someone who zooms out
specifically to see more cards at once (e.g. to eyeball a whole category's
completeness) gets no benefit from doing so.

## What Changes

- Replace the standard category grid's fixed `repeat(5, 1fr)` column layout
  with a responsive layout that fits as many tiles per row as the available
  width allows, driven by a fixed minimum tile width rather than a fixed
  column count.
- Cap the responsive layout at 15 tiles per row, so zooming out arbitrarily
  far does not shrink tiles below a usable size.
- Preserve today's ~5-tiles-per-row appearance at typical desktop zoom
  (100%) and viewport widths, so this is a zoom/wide-viewport enhancement,
  not a visual change at default zoom.
- Leave the puzzle sub-group grid (fixed 3 tiles per row) unchanged — it
  represents an actual jigsaw layout, not a density preference, and remains
  out of scope for this change.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-gallery-page`: the "Category Section Grid Density" requirement's
  fixed "5 tiles per row" rule for non-puzzle categories changes to a
  responsive tile count that grows with available width up to a maximum of
  15 tiles per row.

## Impact

- `NinjagoScanner.Web/wwwroot/app.css`: `.gallery-grid-standard` rule (and
  its mobile breakpoint override) change from a fixed column count to a
  responsive `auto-fill`/`minmax`-based layout with an upper bound on column
  count.
- `NinjagoScanner.Web/Components/Pages/Gallery.razor`: no markup changes
  expected — the grid's DOM structure and per-tile markup are unaffected;
  only the CSS layout rule changes.
- No changes to CatalogService, PictureService, gRPC contracts, or any
  server-side data shape.
