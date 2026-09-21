# catalog-service-series-catalog Specification

## Purpose

Lets clients discover and look up the catalog's card series — name, year, special features, special editions, and optionally known card names — over the `CardCatalog` gRPC service.

## Requirements

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

### Requirement: Look up a single series by name
`GetSeries` SHALL return the series entry matching the requested series name, using a normalized comparison that ignores case, leading/trailing whitespace, underscores, hyphens, and repeated internal whitespace, and SHALL indicate when no match is found.

#### Scenario: Series found
- **WHEN** a client calls `GetSeries` with a `series_name` that matches an existing series after normalization (e.g. different case, underscores instead of spaces, or extra whitespace)
- **THEN** the response has `found = true` and `series` populated with that series' entry

#### Scenario: Series not found
- **WHEN** a client calls `GetSeries` with a `series_name` that matches no known series after normalization
- **THEN** the response has `found = false` and `series` left unset

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

### Requirement: Series names are unique across detail files
The catalog SHALL treat two series entries whose names are the same after normalization (ignoring case, underscores, hyphens and repeated whitespace) as a data error and SHALL fail loading with a catalog data error naming the duplicate series and the file it was found in. It SHALL NOT silently keep one of them and discard the other.

#### Scenario: Two detail files declare the same series
- **WHEN** two `series_*.json` files each declare a series that normalizes to the same name (e.g. `Serie_1` and `Serie 1`)
- **THEN** loading the catalog fails with a catalog data error that names the duplicate series

#### Scenario: Distinct series in separate files load side by side
- **WHEN** two `series_*.json` files declare series with different names (e.g. `Serie_0` and `Serie_1`)
- **THEN** `ListSeries` returns both, each with its own cards

### Requirement: Series 0 is a distinct series listed first
The shipped catalog SHALL expose the early Spinner-set series as its own series named "Serie 0" with `sort_order` 0, separate from "Serie 1", so it is returned before every other series by `ListSeries`.

#### Scenario: Series 0 and Series 1 are both listed
- **WHEN** a client calls `ListSeries` against the shipped catalog data
- **THEN** the response contains a "Serie 0" entry and a "Serie 1" entry, and "Serie 0" appears before "Serie 1"

#### Scenario: Series 0 lists its real cards
- **WHEN** a client lists all cards from the shipped catalog data
- **THEN** "Serie 0" has 221 cards: Wave 1 numbers 1-81 and `LE1`-`LE9`, Wave 2 numbers `WB1`-`WB125`, `WBLE1`-`WBLE6`

#### Scenario: Series 1 keeps its cards
- **WHEN** a client lists all cards from the shipped catalog data
- **THEN** every card of "Serie 1" is present and none of them has been replaced by a "Serie 0" card

### Requirement: Card numbers are unique within a series
Every card of a series SHALL have a card number that is unique within that series after normalization, so that a series + card-number pair identifies exactly one catalog card. Where a series contains several waves that each number from 1 (Series 0), the later wave's numbers SHALL carry a letters-only prefix (`WB`) that survives normalization and sorts correctly.

#### Scenario: Series 0 waves do not collide
- **WHEN** the shipped catalog data is loaded
- **THEN** no two "Serie 0" cards share a card number, and Wave 2 cards are numbered `WB1`-`WB125` rather than 1-125
