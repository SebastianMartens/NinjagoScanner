## MODIFIED Requirements

### Requirement: All photos in a group are shown at once
The review page SHALL display, simultaneously, every photo currently in the selected group up to a maximum of 18 photos, with each displayed photo tile always showing that photo's own current series name, card name, and card number. Photo tiles SHALL be arranged in a grid of at most six tiles per row, with tile size unchanged from the page's default tile size regardless of how many tiles fit in a row; on viewports too narrow to fit six tiles at that size, the grid SHALL wrap to fewer tiles per row instead of shrinking the tiles. When a group contains more than 18 photos, the review page SHALL display only the first 18 photos in the group's existing order.

#### Scenario: Viewing a group with multiple photos at or under the cap
- **WHEN** a user opens a group containing more than one photo but no more than 18
- **THEN** all of that group's photos are shown at the same time, each labeled with its own series name, card name, and card number

#### Scenario: Viewing a group with more photos than the display cap
- **WHEN** a user opens a group containing more than 18 photos
- **THEN** only the first 18 photos, in the group's existing order, are shown as tiles

#### Scenario: A wide viewport does not exceed six tiles per row
- **WHEN** a user views a group with more than six displayed photos on a viewport wide enough to fit more than six tiles at the default tile size
- **THEN** no row shows more than six photo tiles, and any additional displayed photos wrap to a new row

#### Scenario: Tile size is unaffected by row width
- **WHEN** a user views a group's photo grid on any viewport width
- **THEN** each photo tile keeps the page's default tile size rather than growing to fill unused row width or shrinking to fit more tiles into a row

#### Scenario: A narrow viewport wraps to fewer tiles per row
- **WHEN** a user views a group's photo grid on a viewport too narrow to fit six tiles at the default tile size
- **THEN** the grid shows fewer tiles per row, wrapping remaining displayed photos to additional rows, rather than shrinking tile size to fit six per row

## ADDED Requirements

### Requirement: A message indicates when a group has more photos than are shown
When a group contains more photos than the review page's 18-photo display cap, the review page SHALL show a message stating that more photos exist for that group beyond the ones currently displayed. The message SHALL NOT be shown for a group at or under the cap.

#### Scenario: A group exceeding the cap shows a message
- **WHEN** a user opens a group containing more than 18 photos
- **THEN** the review page shows a message indicating that the group has more photos than are currently displayed

#### Scenario: A group at or under the cap shows no message
- **WHEN** a user opens a group containing 18 or fewer photos
- **THEN** the review page shows no such message
