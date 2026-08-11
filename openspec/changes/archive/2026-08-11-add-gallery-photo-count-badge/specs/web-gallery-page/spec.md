## ADDED Requirements

### Requirement: Card Tile Shows Photo Count Badge
Each non-puzzle Gallery card tile that shows a matched photo SHALL display a
small badge in the tile's upper-right corner containing the number of photos
matched to that card (`OwnedCopies`, as also used on the Collection page).
Placeholder tiles (no matched photo) SHALL NOT show this badge. Tiles within
a puzzle sub-group SHALL NOT show this badge, per the "Puzzle Tiles Show
Only the Photo or Placeholder Graphic" requirement.

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
