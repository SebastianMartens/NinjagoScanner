## ADDED Requirements

### Requirement: Tile Click Opens the Card in the Review Page
Clicking or tapping a card tile that shows a photo SHALL navigate to the Review page, opened on the group for that tile's card (its series and card number), so the user sees every photo scanned for that card and can review or correct them there. This applies to every category, puzzle sub-groups included. The Gallery page SHALL NOT open an in-place photo zoom or any other overlay for a tile. Placeholder tiles SHALL NOT be interactive.

#### Scenario: Clicking a photo tile
- **WHEN** the user clicks a card tile that shows a photo
- **THEN** the system navigates to the Review page, showing the group for that tile's series and card number

#### Scenario: Clicking a puzzle photo tile
- **WHEN** the user clicks a tile within a puzzle sub-group that shows a photo
- **THEN** the system navigates to the Review page, showing the group for that tile's series and card number, exactly as for a non-puzzle tile

#### Scenario: Clicking a placeholder tile
- **WHEN** the user clicks a card tile that shows a placeholder (no photo)
- **THEN** the system does not navigate and takes no action

### Requirement: Puzzle Tiles Show No Caption or Card Name
Card tiles within a puzzle sub-group section SHALL show nothing but the
matched photo (for photo tiles) or the placeholder graphic (for placeholder
tiles) — no caption element of any kind, and no card name anywhere on the
tile. A puzzle placeholder tile (no matched photo) SHALL show the card's
number inside the placeholder graphic itself, not as a separate caption, so
pieces remain distinguishable before they are scanned. Clicking a puzzle
photo tile behaves as for every other photo tile, per the "Tile Click Opens
the Card in the Review Page" requirement.

#### Scenario: Puzzle photo tile
- **WHEN** a card in a puzzle sub-group has a matched photo
- **THEN** its tile shows only the photo, with no caption and no card name or
  number appended below or over it

#### Scenario: Puzzle placeholder tile
- **WHEN** a card in a puzzle sub-group has no matched photo
- **THEN** its tile shows only a placeholder graphic labeled with the card's
  number inside the graphic itself, with no separate caption element and no
  card name anywhere on the tile

### Requirement: Non-Puzzle Card Tile Reflects a Flagged Photo's Review Status
A non-puzzle Gallery card tile whose matched photo currently has `ReviewStatus` `incorrect` SHALL visually indicate that flagged state on the tile. The indicator is read-only: the Gallery page offers no way to change a photo's `ReviewStatus` (that happens on the Review page). A puzzle sub-group tile SHALL NOT show this indicator, consistent with puzzle tiles showing nothing but the photo.

#### Scenario: Tile shows the flagged state
- **WHEN** a non-puzzle card tile's matched photo has `ReviewStatus` `incorrect`
- **THEN** the tile visually indicates that the photo is flagged as Fehlerhaft

#### Scenario: Tile does not show the flagged state for other review statuses
- **WHEN** a card tile's matched photo has `ReviewStatus` `unreviewed` or `verified`
- **THEN** the tile does not show the flagged-state indicator

#### Scenario: Puzzle tile never shows the flagged-state indicator
- **WHEN** a card tile within a puzzle sub-group has a matched photo, regardless of its `ReviewStatus`
- **THEN** the tile does not show the flagged-state indicator

## MODIFIED Requirements

### Requirement: Card Tile Shows Matched Photo or Placeholder
Each card tile SHALL show a photo if at least one scanned photo is matched to
that card (by series and card number), and SHALL show a placeholder tile
containing the card's name if no photo is matched. Placeholder tiles SHALL occupy
the same grid position and size as photo tiles, so section layouts are unaffected
by ownership. This requirement applies to non-puzzle categories; puzzle sub-group
tiles follow the "Puzzle Tiles Show No Caption or Card Name"
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

### Requirement: Card Tile Shows Photo Count Badge
Each non-puzzle Gallery card tile that shows a matched photo SHALL display a
small badge in the tile's upper-right corner containing the number of photos
matched to that card (`OwnedCopies`, as also used on the Collection page).
Placeholder tiles (no matched photo) SHALL NOT show this badge. Tiles within
a puzzle sub-group SHALL NOT show this badge, per the "Puzzle Tiles Show No Caption or Card Name" requirement.

#### Scenario: Card has exactly one matched photo
- **WHEN** a non-puzzle card tile shows a photo because exactly one photo is
  matched to that card
- **THEN** the tile displays a badge in its upper-right corner showing "1"

#### Scenario: Card has multiple matched photos
- **WHEN** a non-puzzle card tile shows a photo because more than one photo
  is matched to that card
- **THEN** the tile displays a badge in its upper-right corner showing the
  total count of matched photos for that card

#### Scenario: Card has no matched photo
- **WHEN** a non-puzzle card tile shows a placeholder because no photo is
  matched to that card
- **THEN** the tile shows no badge

#### Scenario: Puzzle tile with a matched photo
- **WHEN** a card tile within a puzzle sub-group shows a photo
- **THEN** the tile shows no badge, regardless of how many photos are
  matched to that card

## REMOVED Requirements

### Requirement: Tile Click Opens In-Place Photo Zoom
**Reason**: Clicking a photo tile now opens the Review page on that card instead of an in-place lightbox.
**Migration**: See the "Tile Click Opens the Card in the Review Page" requirement.

### Requirement: Puzzle Tiles Show Only the Photo or Placeholder Graphic
**Reason**: Renamed and reworded; the lightbox it referred to no longer exists.
**Migration**: See "Puzzle Tiles Show No Caption or Card Name".

### Requirement: Card Tile Reflects a Flagged Photo's Review Status
**Reason**: Renamed and reworded; the lightbox-based indicator for puzzle tiles no longer exists.
**Migration**: See "Non-Puzzle Card Tile Reflects a Flagged Photo's Review Status".

### Requirement: Card Tile With a Matched Photo Provides a Fehlerhaft Control
**Reason**: With the improved AI analysis, flagging a photo as Fehlerhaft is no longer a common enough action to earn a control on every Gallery tile. The Review page already offers the `Incorrect` status for every photo, and a Gallery tile now leads there directly.
**Migration**: Click the card's photo tile to open the Review page on that card's group and set the photo's status to `Incorrect` with the status control there.
