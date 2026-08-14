## MODIFIED Requirements

### Requirement: Series without a logo mapping fall back to text only
When a series has no entry in the logo mapping, the Review page's series-reassignment grid SHALL render that series' cell as plain text-only, identical to current behavior, without any icon placeholder or broken-image indicator.

#### Scenario: Series has no logo mapping
- **WHEN** the Review page renders a series-reassignment grid cell for a series that has no entry in the logo mapping (e.g. `Serie 1`)
- **THEN** the cell shows only the series name label, with no icon element rendered

#### Scenario: Series exists in the catalog but its logo has not yet been mapped
- **WHEN** the Review page renders a series-reassignment grid cell for a series returned by the catalog service that has not yet been added to the logo mapping
- **THEN** the cell behaves identically to a series with no official logo - text only, no icon, no error
