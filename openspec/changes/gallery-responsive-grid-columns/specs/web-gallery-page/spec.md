## MODIFIED Requirements

### Requirement: Category Section Grid Density
Each category section SHALL render its cards in a grid using the catalog's
existing card order within that category. A category whose name identifies it
as a puzzle sub-group (the catalog's "Puzzle Cards" grouping) SHALL render a
fixed 3 card tiles per row. Every other category SHALL render a responsive
number of card tiles per row that grows with the horizontal space available to
the grid — whether from a wider viewport or the user zooming out the browser
page — starting at 5 tiles per row at the page's default width and zoom level,
and increasing up to a maximum of 15 tiles per row. Tiles SHALL NOT be
rendered below a minimum usable size; once 15 tiles per row are shown,
additional available width SHALL add margin rather than further tiles or
smaller tiles. The puzzle sub-group grid SHALL use a smaller gap between
tiles and a smaller tile corner radius than every other category's grid, so
adjoining puzzle pieces read as one image rather than a row of
separately-framed cards, and SHALL remain unaffected by the standard grid's
responsive behavior. Non-puzzle grids SHALL keep their existing gap and
corner radius unchanged.

#### Scenario: Standard category grid
- **WHEN** a section is rendered for a non-puzzle category at the page's
  default width and zoom level
- **THEN** its card tiles wrap at 5 tiles per row, using the standard gap and
  corner radius

#### Scenario: Standard category grid with more available width
- **WHEN** a section is rendered for a non-puzzle category and more
  horizontal space is available than at the default width and zoom level
  (for example, because the user zoomed out the page)
- **THEN** more than 5 card tiles are shown per row, up to a maximum of 15,
  without the tiles shrinking below a minimum usable size

#### Scenario: Standard category grid at maximum density
- **WHEN** even more horizontal space becomes available beyond what is
  needed to show 15 tiles per row
- **THEN** the grid continues to show 15 tiles per row rather than showing
  more, smaller tiles

#### Scenario: Puzzle sub-group grid
- **WHEN** a section is rendered for a "Puzzle Cards" sub-group category
- **THEN** its card tiles wrap at 3 tiles per row, so a 9-card sub-puzzle forms an
  exact 3x3 grid and larger sub-puzzles form additional full rows of 3
- **AND** the gap between tiles and the tiles' corner radius are both smaller
  than the standard category grid's
- **AND** the puzzle grid's column count is unaffected by any additional
  horizontal space that would grow the standard grid's column count
