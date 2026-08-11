## MODIFIED Requirements

### Requirement: Category Section Grid Density
Each category section SHALL render its cards in a grid using the catalog's
existing card order within that category. A category whose name identifies it as
a puzzle sub-group (the catalog's "Puzzle Cards" grouping) SHALL render 3 card
tiles per row. Every other category SHALL render 5 card tiles per row. The
puzzle sub-group grid SHALL use a smaller gap between tiles and a smaller tile
corner radius than every other category's grid, so adjoining puzzle pieces read
as one image rather than a row of separately-framed cards. Non-puzzle grids
SHALL keep their existing gap and corner radius unchanged.

#### Scenario: Standard category grid
- **WHEN** a section is rendered for a non-puzzle category
- **THEN** its card tiles wrap at 5 tiles per row, using the standard gap and
  corner radius

#### Scenario: Puzzle sub-group grid
- **WHEN** a section is rendered for a "Puzzle Cards" sub-group category
- **THEN** its card tiles wrap at 3 tiles per row, so a 9-card sub-puzzle forms an
  exact 3x3 grid and larger sub-puzzles form additional full rows of 3
- **AND** the gap between tiles and the tiles' corner radius are both smaller
  than the standard category grid's

### Requirement: Card Tile Shows Matched Photo or Placeholder
Each card tile SHALL show a photo if at least one scanned photo is matched to
that card (by series and card number), and SHALL show a placeholder tile
containing the card's name if no photo is matched. Placeholder tiles SHALL occupy
the same grid position and size as photo tiles, so section layouts are unaffected
by ownership. This requirement applies to non-puzzle categories; puzzle sub-group
tiles follow the "Puzzle Tiles Show Only the Photo or Placeholder Graphic"
requirement instead, which overrides the placeholder's fallback content and
removes the caption entirely.

#### Scenario: Card has a matched photo
- **WHEN** a catalog card has one or more scanned photos matched to it
- **THEN** its tile shows one of the matched photos as a thumbnail

#### Scenario: Card has no matched photo
- **WHEN** a catalog card has no scanned photo matched to it and its category is
  not a puzzle sub-group
- **THEN** its tile shows a placeholder containing the card's name instead of an
  image

#### Scenario: Card has multiple matched photos
- **WHEN** a catalog card has more than one scanned photo matched to it
- **THEN** the tile deterministically shows the same one of those photos on every
  render, without requiring the user to pick

## ADDED Requirements

### Requirement: Puzzle Tiles Show Only the Photo or Placeholder Graphic
Card tiles within a puzzle sub-group section SHALL show nothing but the
matched photo (for photo tiles) or the placeholder graphic (for placeholder
tiles) — no caption element of any kind, and no card name anywhere on the
tile. A puzzle placeholder tile (no matched photo) SHALL show the card's
number inside the placeholder graphic itself, not as a separate caption, so
pieces remain distinguishable before they are scanned. This requirement does
not change the lightbox opened by clicking a photo tile, which continues to
caption the enlarged photo with the card's name per the "Tile Click Opens
In-Place Photo Zoom" requirement.

#### Scenario: Puzzle photo tile
- **WHEN** a card in a puzzle sub-group has a matched photo
- **THEN** its tile shows only the photo, with no caption and no card name or
  number appended below or over it

#### Scenario: Puzzle placeholder tile
- **WHEN** a card in a puzzle sub-group has no matched photo
- **THEN** its tile shows only a placeholder graphic labeled with the card's
  number inside the graphic itself, with no separate caption element and no
  card name anywhere on the tile

#### Scenario: Opening the lightbox from a puzzle photo tile
- **WHEN** the user clicks a puzzle tile that shows a photo
- **THEN** the system opens the in-place lightbox and captions it with the
  card's name, unaffected by the tile itself showing no caption
