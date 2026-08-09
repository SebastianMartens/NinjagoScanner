## ADDED Requirements

### Requirement: A per-series collection summary is shown
The Overview page SHALL show, for every series known to the catalog, ordered by the catalog's `SortOrder` ascending: the total number of catalog cards in that series, the number of distinct card numbers in that series with at least one matching photo, and the total number of matching photos for that series (including duplicates). A photo matches a series only when its Series Name equals the series's name exactly after trimming whitespace and case-folding — a photo whose series-name resolution failed during AI Analysis carries its unresolved raw guess instead, so it will not match any series here. This remains a separate rule from the normalized matching `/collection`'s `Owned Copies` uses. Category is not part of this summary.

#### Scenario: A series with no owned cards
- **WHEN** a catalog series has no photo whose Series Name exactly matches it (after trim/case-fold)
- **THEN** the series is still shown, with an owned-card count and a photo count of zero

#### Scenario: A Next Level variant is shown separately from its base series
- **WHEN** the catalog contains both a series and its Next Level variant (e.g. "Serie 5" and "Serie 5 Next Level")
- **THEN** both appear as separate entries in the summary, each with their own counts

### Requirement: Photos with an unrecognized series name are counted separately
Photos whose Series Name doesn't exactly match (after trim/case-fold) any catalog series SHALL be counted in a separate unknown-series bucket, shown apart from the per-series entries and only when its count is greater than zero, mirroring how `/collection` already omits its "nicht zugeordnet" statistic at zero.

#### Scenario: Photos with an unrecognized series name exist
- **WHEN** at least one photo's Series Name does not exactly match (after trim/case-fold) any catalog series
- **THEN** the unknown-series bucket is shown with that count, separate from the per-series entries

#### Scenario: Every photo matches a known series
- **WHEN** every photo's Series Name exactly matches (after trim/case-fold) some catalog series
- **THEN** the unknown-series bucket is omitted

### Requirement: The series summary layout can be switched between tile and table views
The Overview page SHALL provide a control that switches the per-series summary between a tile/card grid and a table, defaulting to the tile/card grid on first load. The chosen layout SHALL NOT persist across page reloads or revisits.

#### Scenario: Switching layout
- **WHEN** a user toggles the layout control
- **THEN** the per-series summary re-renders in the selected layout immediately, without a page reload

#### Scenario: Reloading resets the layout
- **WHEN** a user reloads or revisits the Overview page after switching layouts
- **THEN** the summary is shown in the default tile/card grid layout again

### Requirement: Selecting a series navigates to the pre-filtered collection list
Selecting a series entry in the per-series summary SHALL navigate to the collection list page (`/collection`) with that series pre-selected via a query-string parameter.

#### Scenario: Clicking a series entry
- **WHEN** a user selects a series entry in the per-series summary
- **THEN** the browser navigates to `/collection` with a `series` query-string parameter set to that series' exact name, and the collection list shows that series pre-selected in its series filter
