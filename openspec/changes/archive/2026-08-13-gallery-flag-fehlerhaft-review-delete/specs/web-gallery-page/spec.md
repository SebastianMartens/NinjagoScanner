## ADDED Requirements

### Requirement: Card Tile With a Matched Photo Provides a Fehlerhaft Control
Each Gallery card tile that shows a matched photo SHALL provide a way to set that photo's `ReviewStatus` to `incorrect` ("Fehlerhaft") without navigating away from the Gallery page. A non-puzzle tile SHALL show this as a "Fehlerhaft" control directly on the tile. A puzzle sub-group tile SHALL NOT show this control on the tile itself, consistent with the "Puzzle Tiles Show Only the Photo or Placeholder Graphic" requirement; instead, the lightbox opened for that tile SHALL provide the control, since opening the lightbox is that tile's existing "select this card" interaction. Placeholder tiles (no matched photo) SHALL NOT show this control anywhere - neither on the tile nor in a lightbox, since no lightbox can be opened for a tile with no photo.

#### Scenario: Flagging a non-puzzle card's matched photo from its tile
- **WHEN** a user activates the "Fehlerhaft" control on a non-puzzle card tile showing a matched photo
- **THEN** that photo's `ReviewStatus` is set to `incorrect`, and the Gallery page underneath remains displayed

#### Scenario: Placeholder tile has no Fehlerhaft control
- **WHEN** a card tile shows a placeholder because no photo is matched to that card
- **THEN** the tile does not show a "Fehlerhaft" control, and no lightbox is available to show one either

#### Scenario: Puzzle tile has no Fehlerhaft control directly on the tile
- **WHEN** a card tile within a puzzle sub-group shows a matched photo
- **THEN** the tile itself does not show a "Fehlerhaft" control

#### Scenario: Flagging a puzzle card's matched photo from its lightbox
- **WHEN** a user opens the lightbox for a puzzle sub-group tile showing a matched photo and activates the "Fehlerhaft" control shown there
- **THEN** that photo's `ReviewStatus` is set to `incorrect`

### Requirement: Card Tile Reflects a Flagged Photo's Review Status
A non-puzzle Gallery card tile whose matched photo currently has `ReviewStatus` `incorrect` SHALL visually indicate that flagged state on the tile. A puzzle sub-group tile SHALL NOT show this indicator on the tile itself, consistent with showing no Fehlerhaft control there; the lightbox opened for a puzzle tile SHALL instead indicate the flagged state on its own Fehlerhaft control.

#### Scenario: Tile shows the flagged state
- **WHEN** a non-puzzle card tile's matched photo has `ReviewStatus` `incorrect`
- **THEN** the tile visually indicates that the photo is flagged as Fehlerhaft

#### Scenario: Tile does not show the flagged state for other review statuses
- **WHEN** a card tile's matched photo has `ReviewStatus` `unreviewed` or `verified`
- **THEN** the tile does not show the flagged-state indicator

#### Scenario: Puzzle tile never shows the flagged-state indicator on the grid
- **WHEN** a card tile within a puzzle sub-group has a matched photo, regardless of its `ReviewStatus`
- **THEN** the tile does not show the flagged-state indicator

#### Scenario: A puzzle card's lightbox indicates an already-flagged photo
- **WHEN** a user opens the lightbox for a puzzle tile whose matched photo has `ReviewStatus` `incorrect`
- **THEN** the lightbox's Fehlerhaft control indicates that the photo is already flagged
