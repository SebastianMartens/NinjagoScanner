# web-gallery-page Specification

## Purpose

Lets a user visually browse one series of the catalog as a photo grid, grouped by
category, to see at a glance what's owned versus missing — a complement to
Collection's row-by-row editing table.

## Requirements

### Requirement: Series Selection Is Mandatory and Single-Valued
The Gallery page SHALL require the user to select exactly one series before
rendering any card grid. The series selector SHALL NOT offer an "all series"
option.

#### Scenario: No series selected yet
- **WHEN** the user opens the Gallery page without a previously selected series
- **THEN** the system prompts the user to choose a series and does not render any
  category sections or card tiles

#### Scenario: Series selected
- **WHEN** the user selects a series from the series dropdown
- **THEN** the system renders that series' cards, grouped into category sections,
  and no cards from any other series appear on the page

### Requirement: Optional Category Filter
The Gallery page SHALL offer a category filter, scoped to the categories present
in the currently selected series. Leaving the filter unset SHALL show every
category of the selected series as its own section, ordered by each category's
lowest card number (not alphabetically by category label), consistent with the
base card ordering used everywhere in the application: card number takes
precedence over category, since categories are printed groupings rather than a
numbering scheme, and an alphabetically-early category can start well into a
series' number range. Selecting a category SHALL show only that category's
section. The category filter's own option order follows the same rule.

#### Scenario: No category selected
- **WHEN** a series is selected and no category filter is applied
- **THEN** the system renders one section per category that exists in that
  series, each labeled with its category name, ordered by each category's
  lowest card number

#### Scenario: An alphabetically-early category that starts later in the number range does not appear first
- **WHEN** the selected series has a category (e.g. "Action Cards") whose
  lowest card number is higher than another category's (e.g. "Heroes"
  starting at card 1), even though the first category's name would sort
  alphabetically earlier
- **THEN** the section for the category with the lower starting card number
  appears first, and the category filter's dropdown lists it first too

#### Scenario: Category selected
- **WHEN** the user selects a specific category from the category filter
- **THEN** the system renders only that category's section

### Requirement: Category Section Grid Density
Each category section SHALL render its cards in a grid using the catalog's
existing card order within that category. A category whose name identifies it as
a puzzle sub-group (the catalog's "Puzzle Cards" grouping) SHALL render 3 card
tiles per row. Every other category SHALL render 5 card tiles per row.

#### Scenario: Standard category grid
- **WHEN** a section is rendered for a non-puzzle category
- **THEN** its card tiles wrap at 5 tiles per row

#### Scenario: Puzzle sub-group grid
- **WHEN** a section is rendered for a "Puzzle Cards" sub-group category
- **THEN** its card tiles wrap at 3 tiles per row, so a 9-card sub-puzzle forms an
  exact 3x3 grid and larger sub-puzzles form additional full rows of 3

### Requirement: Card Tile Shows Matched Photo or Placeholder
Each card tile SHALL show a photo if at least one scanned photo is matched to
that card (by series and card number), and SHALL show a placeholder tile
containing the card's name if no photo is matched. Placeholder tiles SHALL occupy
the same grid position and size as photo tiles, so section layouts are unaffected
by ownership.

#### Scenario: Card has a matched photo
- **WHEN** a catalog card has one or more scanned photos matched to it
- **THEN** its tile shows one of the matched photos as a thumbnail

#### Scenario: Card has no matched photo
- **WHEN** a catalog card has no scanned photo matched to it
- **THEN** its tile shows a placeholder containing the card's name instead of an
  image

#### Scenario: Card has multiple matched photos
- **WHEN** a catalog card has more than one scanned photo matched to it
- **THEN** the tile deterministically shows the same one of those photos on every
  render, without requiring the user to pick

### Requirement: Tile Click Opens In-Place Photo Zoom
Clicking or tapping a card tile that shows a photo SHALL open an in-place
lightbox displaying that photo enlarged, captioned with the card's name, without
navigating away from the Gallery page. Placeholder tiles SHALL NOT be
interactive.

#### Scenario: Clicking a photo tile
- **WHEN** the user clicks a card tile that shows a photo
- **THEN** the system opens an in-place lightbox showing the enlarged photo with
  the card name as a caption, and the Gallery page underneath remains unchanged

#### Scenario: Clicking a placeholder tile
- **WHEN** the user clicks a card tile that shows a placeholder (no photo)
- **THEN** the system does not open a lightbox and takes no action

#### Scenario: Closing the lightbox
- **WHEN** the user dismisses the open lightbox
- **THEN** the system returns to the unchanged Gallery grid with no page
  navigation having occurred

### Requirement: Gallery Is Reachable From Navigation
The main navigation SHALL include a link to the Gallery page.

#### Scenario: Navigating to the Gallery page
- **WHEN** the user selects the Gallery entry in the main navigation
- **THEN** the system displays the Gallery page
