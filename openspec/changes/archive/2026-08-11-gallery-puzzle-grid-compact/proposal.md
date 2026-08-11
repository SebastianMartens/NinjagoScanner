## Why

Puzzle sub-groups in the Gallery already render as a dense 3-column grid, but
they still use the same tile chrome as standard categories: a name caption,
a 1rem gap, and 16px-radius corners. That styling was designed for
identifying single cards spread across a wide grid, not for a puzzle image
that is meant to be assembled from adjoining pieces — the caption and
spacing make the puzzle read as a list of separate cards instead of one
picture split into 9 (or more) pieces.

## What Changes

- Puzzle sub-group photo tiles (grid `gallery-grid-puzzle`) show only the
  photo — no caption element at all, so neither the card name nor its
  number appears on the tile.
- Puzzle placeholder tiles (no matched photo) show only the placeholder
  graphic, with the card number rendered inside that graphic instead of the
  name, so there is still a way to tell pieces apart before they're
  scanned — but with no separate caption below it either.
- The grid gap between puzzle tiles is reduced from the standard `1rem` to a
  small fixed value, distinct from the standard grid's gap.
- Puzzle tile corners use a small border-radius instead of the standard
  16px, so adjoining pieces read as one image rather than a row of separate
  cards.
- Standard (non-puzzle) category grids are unaffected: they keep the name
  caption, the existing gap, and the existing corner radius.

## Capabilities

### Modified Capabilities
- `web-gallery-page`: puzzle sub-group tiles change what they display (no
  caption on photo tiles; card number inside the graphic on placeholders,
  no name anywhere) and how they're styled (smaller gap, smaller corner
  radius), scoped strictly to puzzle sections.

## Impact

- `NinjagoScanner.Web/Components/Pages/Gallery.razor`: puzzle photo tiles
  drop the caption element entirely; puzzle placeholder tiles stop
  rendering `card.CardName` and render `card.CardNumber` inside the
  placeholder graphic instead, with no separate caption.
- `NinjagoScanner.Web/wwwroot/app.css`: `.gallery-grid-puzzle` needs its own
  `gap` value, and puzzle tiles need a distinct border-radius rule that
  overrides the standard `.gallery-tile` radius without affecting standard
  grids.
- No API, data model, or catalog changes — this is presentation-only within
  the existing Gallery page.
