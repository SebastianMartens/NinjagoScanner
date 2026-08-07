# catalog-service-series-catalog Specification

## Purpose

Lets clients discover and look up the catalog's card series — name, year, special features, special editions, and optionally known card names — over the `CardCatalog` gRPC service.

## Requirements

### Requirement: List all series
`ListSeries` SHALL return every series known to the catalog, ordered by year ascending, then by series name alphabetically (case-insensitive).

#### Scenario: Listing series without known card names
- **WHEN** a client calls `ListSeries` with `include_known_card_names = false`
- **THEN** the response contains one entry per series with `series_name`, `year`, `special_features`, and `special_editions` populated, and `known_card_names` left empty

#### Scenario: Listing series with known card names
- **WHEN** a client calls `ListSeries` with `include_known_card_names = true`
- **THEN** each returned series entry additionally includes `known_card_names`, populated from that series' detail data

### Requirement: Look up a single series by name
`GetSeries` SHALL return the series entry matching the requested series name, using a normalized comparison that ignores case, leading/trailing whitespace, underscores, hyphens, and repeated internal whitespace, and SHALL indicate when no match is found.

#### Scenario: Series found
- **WHEN** a client calls `GetSeries` with a `series_name` that matches an existing series after normalization (e.g. different case, underscores instead of spaces, or extra whitespace)
- **THEN** the response has `found = true` and `series` populated with that series' entry

#### Scenario: Series not found
- **WHEN** a client calls `GetSeries` with a `series_name` that matches no known series after normalization
- **THEN** the response has `found = false` and `series` left unset

### Requirement: Series entries are built entirely from per-series detail data
The system SHALL build each series entry (name, year, special features, special editions, known card names) from that series' own detail file (`series_*.json`). There is no separate main series catalog file — `series.json` has been retired, and its former content (year, special features/`Besonderheiten`, special editions/`Sondereditionen`, and per-limited-edition-card find location/release date) now lives in the corresponding detail file.

#### Scenario: Series year, features, and editions come from the detail file
- **WHEN** a series' detail file provides `Jahr`, `Besonderheiten`, and `Sondereditionen`
- **THEN** `ListSeries` and `GetSeries` return that series with `year`, `special_features`, and `special_editions` populated from those fields

#### Scenario: Series detail file omits optional metadata
- **WHEN** a series' detail file has no `Jahr`, `Besonderheiten`, or `Sondereditionen`
- **THEN** `ListSeries` and `GetSeries` still return that series, using year `0` and empty `special_features`/`special_editions`
