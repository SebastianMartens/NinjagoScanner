## MODIFIED Requirements

### Requirement: All photos in a group are shown at once
The review page SHALL display every photo currently in the selected group simultaneously, with each photo tile always showing that photo's own current series name, card name, and card number. Photo tiles SHALL be arranged in a grid of at most six tiles per row, with tile size unchanged from the page's default tile size regardless of how many tiles fit in a row; on viewports too narrow to fit six tiles at that size, the grid SHALL wrap to fewer tiles per row instead of shrinking the tiles.

#### Scenario: Viewing a group with multiple photos
- **WHEN** a user opens a group containing more than one photo
- **THEN** all of that group's photos are shown at the same time, each labeled with its own series name, card name, and card number

#### Scenario: A wide viewport does not exceed six tiles per row
- **WHEN** a user views a group with more than six photos on a viewport wide enough to fit more than six tiles at the default tile size
- **THEN** no row shows more than six photo tiles, and any additional photos wrap to a new row

#### Scenario: Tile size is unaffected by row width
- **WHEN** a user views a group's photo grid on any viewport width
- **THEN** each photo tile keeps the page's default tile size rather than growing to fill unused row width or shrinking to fit more tiles into a row

#### Scenario: A narrow viewport wraps to fewer tiles per row
- **WHEN** a user views a group's photo grid on a viewport too narrow to fit six tiles at the default tile size
- **THEN** the grid shows fewer tiles per row, wrapping remaining photos to additional rows, rather than shrinking tile size to fit six per row
