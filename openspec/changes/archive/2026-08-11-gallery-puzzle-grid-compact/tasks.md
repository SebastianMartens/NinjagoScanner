## 1. Markup: stop showing the card name on puzzle tiles

- [x] 1.1 In `Gallery.razor`'s photo-tile branch (currently
      `<span class="gallery-tile-caption">Nr. @card.CardNumber &middot;
      @card.CardName</span>`), omit the caption element entirely when
      `section.IsPuzzle` is true (revised after review — an earlier pass kept
      a trimmed "Nr. X" caption, but the tile should show only the photo),
      keeping the existing "Nr. X · Name" caption for standard sections.
- [x] 1.2 In the placeholder-tile branch, when `section.IsPuzzle` is true,
      render `card.CardNumber` in place of `card.CardName` inside
      `gallery-tile-placeholder-name` (or an equivalent element), and drop the
      now-duplicate "Nr. X" caption below it for puzzle placeholders; keep the
      existing name + separate "Nr. X" caption for standard sections.
- [x] 1.3 Add `gallery-tile-puzzle` to the tile's `class` attribute (photo and
      placeholder branches) whenever `section.IsPuzzle` is true, alongside the
      existing `gallery-tile`/`gallery-tile-photo`/`gallery-tile-placeholder`
      classes.

## 2. Styling: compact gap and corner radius for puzzle grids

- [x] 2.1 In `app.css`, set `gap: 0.35rem` on `.gallery-grid-puzzle` (the base
      rule, ~line 1251), leaving `.gallery-grid`'s `gap: 1rem` and
      `.gallery-grid-standard` untouched.
- [x] 2.2 Apply the same `gap: 0.35rem` override to `.gallery-grid-puzzle` in
      the narrow-viewport media query block (~line 1178), so the compact gap
      holds at every viewport width.
- [x] 2.3 Add a new `.gallery-tile-puzzle { border-radius: 4px; }` rule in
      `app.css` near `.gallery-tile` (~line 1256), overriding the standard
      tile's `16px` radius only for tiles that also carry this class.

## 3. Verification

- [x] 3.1 Run the Web app, open Gallery, select a series with a "Puzzle
      Cards" sub-group, and confirm: puzzle photo tiles show only the photo
      (no caption/footer of any kind), puzzle placeholder tiles show only
      the placeholder graphic with the card number inside it (no separate
      caption), the grid gap is visibly tighter than a standard category's
      grid, and puzzle tile corners are only slightly rounded. Verified
      manually by the user in their own running (Visual Studio debug) session
      — confirmed working.
- [x] 3.2 Confirm a standard (non-puzzle) category section is visually
      unchanged: same gap, same corner radius, name still shown in the
      caption/placeholder. Verified manually by the user alongside 3.1.
- [x] 3.3 Click a puzzle photo tile and confirm the lightbox still opens and
      still captions the enlarged photo with the card's name. Verified
      manually by the user alongside 3.1.
- [x] 3.4 Run existing Gallery-related tests (e.g.
      `NinjagoScanner.Web.Tests/Services/CardCatalogServiceGalleryTests.cs`
      and any Gallery component tests) and fix any that assert on puzzle
      placeholder/caption text. All 6 tests passed unchanged — they exercise
      `CardCatalogService`'s data layer, not the Razor markup, so none
      assert on caption/placeholder text.
