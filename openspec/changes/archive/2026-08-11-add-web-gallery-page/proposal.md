## Why

The Web app currently only exposes the catalog as a data table (Collection) or a
photo-review workflow (Review). There is no page for simply browsing what a series
looks like as a wall of card photos — a "does this look right / what's still
missing visually" view distinct from Collection's row-by-row editing focus.

## What Changes

- Add a new `/gallery` page that shows one series' cards as an image grid, grouped
  by category.
- Series selection is a required dropdown (no "all series" option, to keep the
  rendered image count bounded to one series at a time).
- An optional category dropdown narrows the page to a single category section;
  left empty, every category for the selected series renders as its own section.
- Non-puzzle categories render 5 cards per row. Any category under the catalog's
  `Puzzle Cards / <sub-puzzle>` grouping renders 3 cards per row instead, so each
  9-card sub-puzzle reassembles as an exact 3x3 grid (a 3-column wrap generally —
  Series 11's 18-card "Final Showdown" sub-puzzle renders as 3x6 under the same
  rule).
- Cards with a matched photo show that photo as the tile thumbnail; cards without
  one render a placeholder tile showing the card name, in the same grid slot, so
  section layouts stay intact regardless of ownership.
- Clicking/tapping a tile opens a lightbox-style zoom of that photo in place, with
  a card-name caption; no navigation away from the Gallery page. Placeholder tiles
  are not clickable (there is no photo to zoom).
- Add a "Gallery" entry to the main nav.

## Capabilities

### New Capabilities
- `web-gallery-page`: series-scoped, category-grouped photo grid browsing view of
  the catalog, with placeholder tiles for unowned cards and an in-place photo
  lightbox.

### Modified Capabilities
(none — no existing capability's requirements change; the new
`CardCatalogService` lookup and nav entry are implementation details of the new
capability above, not changes to Collection's or Review's documented behavior)

## Impact

- `NinjagoScanner.Web/Components/Pages/`: new `Gallery.razor`.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: new read method that joins
  catalog cards for one series with their first matching photo (by the existing
  series+card-number ownership key), returning a thumbnail URL or null per card —
  see design.md for why the existing overview/detail methods can't be reused
  as-is.
- `NinjagoScanner.Web/Models/`: new model(s) for the gallery projection.
- `NinjagoScanner.Web/Components/Layout/NavMenu.razor`: add "Gallery" link.
- `NinjagoScanner.Web/wwwroot/app.css`: new grid/tile/placeholder/lightbox styles.
- No changes to `NinjagoScanner.CatalogService` or `NinjagoScanner.PictureService`
  — the page consumes existing `CardCatalog` and `CardPictureService` gRPC
  contracts only.
