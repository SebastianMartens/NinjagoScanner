## MODIFIED Requirements

### Requirement: Series reassignment buttons show a mapped logo icon
When a series has a known logo mapping (image and caption), the Review page's series-reassignment popover SHALL display, for that series' grid cell, only the mapped logo image with no visible series-name caption, with the mapped caption exposed as the image's accessible/alt text and the series name exposed as the cell's tooltip (`title`).

#### Scenario: Series has a logo mapping
- **WHEN** the Review page renders a popover grid cell for a series that has an entry in the logo mapping (e.g. `Serie 2`)
- **THEN** the cell shows only the mapped logo image, with no visible series-name label, the image's alt text set to the mapped caption, and the cell's tooltip set to the series name

### Requirement: Series without a logo mapping fall back to text only
When a series has no entry in the logo mapping, the Review page's series-reassignment popover SHALL render that series' grid cell as plain text-only, identical to current behavior, without any icon placeholder or broken-image indicator.

#### Scenario: Series has no logo mapping
- **WHEN** the Review page renders a popover grid cell for a series that has no entry in the logo mapping (e.g. `Serie 1`)
- **THEN** the cell shows only the series name label, with no icon element rendered

#### Scenario: Series exists in the catalog but its logo has not yet been mapped
- **WHEN** the Review page renders a popover grid cell for a series returned by the catalog service that has not yet been added to the logo mapping (e.g. `Serie 10`)
- **THEN** the cell behaves identically to a series with no official logo - text only, no icon, no error

### Requirement: Logo icon does not change reassignment behavior
Adding a logo icon to a series-reassignment popover grid cell SHALL NOT change the cell's activation behavior: activating anywhere on the cell (icon or label) SHALL still reassign the photo's series exactly as it does today.

#### Scenario: Activating a grid cell with a logo icon still reassigns the series
- **WHEN** a user activates a popover grid cell that displays a logo icon
- **THEN** the photo's series is reassigned to that cell's series, identical to activating a text-only cell
