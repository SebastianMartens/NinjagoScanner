## ADDED Requirements

### Requirement: Series lists and groupings follow catalog sort order
Wherever series are listed or cards are grouped by series — the series filter dropdown, the "group by series" view, and manual sorting of the series column — series SHALL appear ordered by the catalog's `SortOrder`, not alphabetically by series name.

#### Scenario: Series filter dropdown ordering
- **WHEN** the collection overview loads its series filter options
- **THEN** the options are ordered by the catalog's `SortOrder` ascending, so a series like "Serie 10" appears after "Serie 2" rather than before it

#### Scenario: Grouping by series
- **WHEN** a user groups the collection by series
- **THEN** the group headers appear ordered by the catalog's `SortOrder` ascending

#### Scenario: Sorting the table by the Series column
- **WHEN** a user clicks the "Serie" column header to sort
- **THEN** rows are ordered by the catalog's `SortOrder` (ascending, or descending when toggled), not by the series name text
