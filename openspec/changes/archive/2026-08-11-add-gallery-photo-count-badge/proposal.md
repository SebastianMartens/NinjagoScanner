## Why

The Gallery page's card tiles show at most one photo per card even when the
user has scanned multiple photos of the same card (duplicates). There's
currently no way to tell, from the Gallery grid, that a card has more than
one matched photo without opening the Collection page's `OwnedCopies` column.
A small badge on the tile surfaces that count directly where the user is
already browsing visually.

## What Changes

- Add a small badge to the upper-right corner of each non-puzzle Gallery card
  tile that shows a matched photo, displaying the number of photos matched to
  that card (its `OwnedCopies` count, reusing the concept already established
  on the Collection page).
- Placeholder tiles (no matched photo) do not show the badge — the
  placeholder itself already communicates zero owned copies.
- Puzzle sub-group tiles are excluded, preserving the existing "no caption
  element of any kind" rule for puzzle tiles.
- `GalleryCardItem` gains a photo-count field, populated from the same
  photo-matching lookup `CardCatalogService.GetGalleryCardsAsync` already
  performs.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `web-gallery-page`: card tiles that show a matched photo now also display a
  small badge with the count of photos matched to that card.

## Impact

- `NinjagoScanner.Web/Models/GalleryCardItem.cs`: new `PhotoCount` property.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`:
  `GetGalleryCardsAsync` populates `PhotoCount` from the existing
  `photosByKey` lookup instead of only taking the first matched photo's URL.
- `NinjagoScanner.Web/Components/Pages/Gallery.razor`: renders the badge on
  non-puzzle photo tiles.
- `NinjagoScanner.Web/wwwroot/app.css`: new badge styling.
