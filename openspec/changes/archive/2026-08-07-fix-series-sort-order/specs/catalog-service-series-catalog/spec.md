## MODIFIED Requirements

### Requirement: List all series
`ListSeries` SHALL return every series known to the catalog, ordered by `sort_order` ascending, then by series name alphabetically (case-insensitive) as a tiebreaker for any series sharing the same `sort_order`.

#### Scenario: Listing series without known card names
- **WHEN** a client calls `ListSeries` with `include_known_card_names = false`
- **THEN** the response contains one entry per series with `series_name`, `year`, `sort_order`, `special_features`, and `special_editions` populated, and `known_card_names` left empty

#### Scenario: Listing series with known card names
- **WHEN** a client calls `ListSeries` with `include_known_card_names = true`
- **THEN** each returned series entry additionally includes `known_card_names`, populated from that series' detail data

#### Scenario: Sort order determines series ordering regardless of series name text
- **WHEN** a series' name would sort differently alphabetically than by its assigned `sort_order` (e.g. "Serie 10" has a lower `sort_order` than "Serie 2")
- **THEN** `ListSeries` returns series in `sort_order` order, not alphabetical series-name order

### Requirement: Series entries are built entirely from per-series detail data
The system SHALL build each series entry (name, year, sort order, special features, special editions, known card names) from that series' own detail file (`series_*.json`). There is no separate main series catalog file — `series.json` has been retired, and its former content (year, special features/`Besonderheiten`, special editions/`Sondereditionen`, and per-limited-edition-card find location/release date) now lives in the corresponding detail file.

#### Scenario: Series year, features, and editions come from the detail file
- **WHEN** a series' detail file provides `Jahr`, `Besonderheiten`, and `Sondereditionen`
- **THEN** `ListSeries` and `GetSeries` return that series with `year`, `special_features`, and `special_editions` populated from those fields

#### Scenario: Series detail file omits optional metadata
- **WHEN** a series' detail file has no `Jahr`, `Besonderheiten`, or `Sondereditionen`
- **THEN** `ListSeries` and `GetSeries` still return that series, using year `0` and empty `special_features`/`special_editions`

#### Scenario: Series sort order comes from the detail file
- **WHEN** a series' detail file provides `SortOrder`
- **THEN** `ListSeries` and `GetSeries` return that series with `sort_order` populated from that field, independent of `year`

#### Scenario: Series detail file omits sort order
- **WHEN** a series' detail file has no `SortOrder`
- **THEN** `ListSeries` and `GetSeries` still return that series, using `sort_order` `0`
