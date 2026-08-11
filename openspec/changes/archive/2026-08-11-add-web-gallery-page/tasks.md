## 1. Gallery data model and service method

- [x] 1.1 Add a `GalleryCardItem` model (`Series`, `Category`, `SortOrder`,
      `CardNumber`, `CardName`, nullable `ImageUrl`) under
      `NinjagoScanner.Web/Models/`.
- [x] 1.2 Add `CardCatalogService.GetGalleryCardsAsync(string series, CancellationToken)`:
      one `LoadCardsFromCatalogServiceAsync` call + one `LoadCardEntriesAsync`
      call, filtered to the requested series, joined via the existing
      `BuildOwnershipKey`, picking the first matched photo ordered by
      `ImageFileName` (same determinism as `BuildCardPhotos`), returning items in
      catalog order (`SortOrder`, then `Category`, then card-number sort key).
- [x] 1.3 Add integration tests for `GetGalleryCardsAsync` in
      `NinjagoScanner.Web.Tests` (reuse `CatalogServiceTestHost` /
      `PictureServiceTestHost`, following the pattern in
      `CardCatalogServiceReviewGroupsTests.cs`): a card with one matched photo, a
      card with no photo (`ImageUrl` is null), a card with multiple matched
      photos (deterministic pick), a "Puzzle Cards / ..." category label passed
      through unchanged, and results scoped to only the requested series.

## 2. Gallery page

- [x] 2.1 Create `NinjagoScanner.Web/Components/Pages/Gallery.razor` at route
      `/gallery`, injecting `CardCatalogService`.
- [x] 2.2 Add the mandatory series `<select>` (populated the same way
      `Collection.razor` populates `availableSeries`), defaulting to the first
      series in sort order, with no "all series" option; support an optional
      `?series=` query parameter on load (mirroring `Collection.razor`'s
      `SeriesQueryParameter`).
- [x] 2.3 Add the optional category `<select>`, scoped to categories present in
      the currently selected series; reset it when it no longer matches the
      newly selected series (same guard `Collection.razor`'s `SelectedSeries`
      setter already applies).
- [x] 2.4 Group the fetched cards into category sections client-side (mirroring
      `Collection.razor`'s `GroupedCards`/`BuildGroups`), ordered alphabetically
      by category label; when a category filter is set, render only that one
      section.
- [x] 2.5 Render each section's cards as a tile grid; mark a section as a puzzle
      section when its category label starts with `"Puzzle Cards"`
      (`StringComparison.OrdinalIgnoreCase`).
- [x] 2.6 Render each tile as either the matched photo (`<img>`) or a placeholder
      showing the card name, in the same tile shape/size either way.

## 3. Lightbox

- [x] 3.1 Add lightbox state (`selectedLightboxCard` or equivalent) and
      open/close handlers to `Gallery.razor`, following the pure-Blazor-state
      pattern already used by `CardsTable.razor`'s `OpenImagePreview`/
      `CloseImagePreview`.
- [x] 3.2 Wire tile click to open the lightbox only for tiles with a matched
      photo; placeholder tiles are not clickable.
- [x] 3.3 Render the lightbox backdrop/dialog with the enlarged photo and a
      card-name caption, closing on backdrop click or an explicit close button,
      without any page navigation.

## 4. Styling

- [x] 4.1 Add gallery grid CSS to `app.css`: category section grids using
      explicit `repeat(5, 1fr)` for normal categories and `repeat(3, 1fr)` for
      puzzle sections, plus placeholder-tile styling (card name, same tile
      footprint as a photo tile).
- [x] 4.2 Add gallery lightbox CSS (new class names, same backdrop/dialog/close/
      caption shape as `.table-image-preview-*`).
- [x] 4.3 Extend the existing `max-width: 700px` breakpoint to collapse the
      gallery grids to fewer columns on narrow viewports.

## 5. Navigation

- [x] 5.1 Add a "Gallery" link to `NinjagoScanner.Web/Components/Layout/NavMenu.razor`.

## 6. Verification

- [x] 6.1 Ran the three services locally and fetched `/gallery` server-rendered
      output against real catalog/photo data: series selection defaults
      correctly and honors `?series=`, category dropdown is scoped and
      alphabetically ordered, 9-card puzzle sub-groups render as exact 3x3
      grids (`gallery-grid-puzzle`), Series 11's 18-card "Final Showdown"
      renders 3x6 under the same rule, non-puzzle categories render 5 per row,
      cards without a matched photo render placeholder tiles with the card
      name, and the nav link and new CSS are served correctly with no server
      exceptions. Did not verify with a real browser (none available in this
      environment): live click-to-open-lightbox/close, the category filter's
      client-side re-render on selection, and the sub-700px responsive
      collapse are implemented following existing, working patterns
      (`CardsTable.razor`'s lightbox, `Collection.razor`'s `@bind` filters) but
      not visually confirmed.
- [x] 6.2 Ran the full test suite (`dotnet test`): 78 passed, 0 failed (17
      PictureService.Tests, 8 Web.Tests including the 5 new gallery tests, 53
      CatalogService.Tests).
