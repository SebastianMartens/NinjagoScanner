## MODIFIED Requirements

### Requirement: Cards can be grouped, filtered, and searched
The overview SHALL let a user group cards by series, category, ownership status (missing / owned once / owned more than once), or not at all; filter by a selected series (which also narrows the available categories) and by category; restrict to only missing or only duplicate-owned cards; and search by card number or card name substring, with all active filters applied together. When grouping by category, and when populating the category filter's options, categories SHALL be ordered by each category's lowest card number (using the catalog's card-number ordering), not alphabetically by category name.

#### Scenario: Selecting a series narrows available categories
- **WHEN** a user selects a series in the series filter
- **THEN** the category filter's options are limited to categories present within that series, and if the previously selected category is no longer available it is cleared

#### Scenario: Combining "only missing" with a series filter
- **WHEN** a user selects a series and also enables "Nur fehlende Karten"
- **THEN** only cards in that series with zero owned copies are shown

#### Scenario: Grouping by category orders groups by lowest card number
- **WHEN** a user groups the collection by category, and one category's lowest card number is higher than another's despite the first category's name sorting alphabetically earlier (e.g. "Action Cards" starting at card `101` versus "Heroes" starting at card `1`)
- **THEN** the "Heroes" group appears before the "Action Cards" group, ordered by each group's lowest card number rather than by category name

#### Scenario: Category filter dropdown lists categories by lowest card number
- **WHEN** the category filter's options are populated for the selected series
- **THEN** the options appear ordered by each category's lowest card number, not alphabetically by category name
