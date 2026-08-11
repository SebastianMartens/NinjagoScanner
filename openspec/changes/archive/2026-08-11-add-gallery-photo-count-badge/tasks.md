## 1. Data model

- [x] 1.1 Add `public int PhotoCount { get; init; }` to `GalleryCardItem`
      (`NinjagoScanner.Web/Models/GalleryCardItem.cs`).

## 2. Service

- [x] 2.1 In `CardCatalogService.GetGalleryCardsAsync`, set
      `PhotoCount = photosByKey[ownershipKey].Count()` when building each
      `GalleryCardItem` (0 when `ownershipKey` is blank), alongside the
      existing `matchedPhoto`/`ImageUrl` lookup.

## 3. UI

- [x] 3.1 In `Gallery.razor`, render a badge `<span>` (e.g.
      `gallery-tile-badge`) inside the photo-tile branch (`card.ImageUrl is
      not null`) only when `!section.IsPuzzle`, showing `@card.PhotoCount`.
- [x] 3.2 Add `.gallery-tile-badge` styling in `wwwroot/app.css` near the
      other `.gallery-tile-*` rules: absolutely positioned in the tile's
      upper-right corner, small font/padding, readable over photo
      thumbnails (background + contrast border/shadow).

## 4. Verification

- [x] 4.1 Run the Web app, open `/gallery`, pick a series with at least one
      card that has 2+ matched photos and one with a single matched photo;
      confirm the badge shows the correct count on both and shows nothing
      on placeholder tiles.
- [x] 4.2 Confirm puzzle sub-group tiles (e.g. a "Puzzle Cards" category)
      show no badge on photo tiles.
- [x] 4.3 Check badge legibility at the standard 5-per-row grid tile size.
