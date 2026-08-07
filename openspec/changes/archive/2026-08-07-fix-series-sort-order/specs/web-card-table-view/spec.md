## MODIFIED Requirements

### Requirement: Rows can be grouped
The table view SHALL let a user group rows by set/series, by `AnalysisStatus`, by rarity, or not at all, with each group header showing the group name and card count. When grouped by status or rarity, groups SHALL be sorted alphabetically by group key. When grouped by set/series, groups for known catalog series SHALL be sorted by the catalog's `SortOrder`, with any set name that does not match a known catalog series sorted after all known series and ordered alphabetically among themselves. Within every grouping, rows SHALL be ordered by set name, then card number, then card name.

#### Scenario: Grouping by status
- **WHEN** a user selects the "status" grouping option
- **THEN** rows are grouped into sections keyed by the card's status label, sorted alphabetically, each section header showing the group name and the number of cards in it

#### Scenario: Grouping by set/series
- **WHEN** a user selects the "set/series" grouping option
- **THEN** groups for known catalog series appear ordered by the catalog's `SortOrder` ascending, and groups for any unrecognized set name appear after all known series, ordered alphabetically

#### Scenario: Cards with no matching catalog series are grouped last
- **WHEN** cards have no set assigned, or a set name that isn't a known catalog series
- **THEN** their group appears after every known-series group
