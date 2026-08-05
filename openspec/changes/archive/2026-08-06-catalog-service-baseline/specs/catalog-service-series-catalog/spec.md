## Purpose

Lets clients discover and look up the catalog's card series — name, year, special features, special editions, and optionally known card names — over the `CardCatalog` gRPC service.

## ADDED Requirements

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

### Requirement: Series entries merge main catalog and detail data
The system SHALL build each series entry by merging the main series catalog (name, year, special features, special editions) with the corresponding per-series detail data (known card names), including a series when it appears in only one of the two sources.

#### Scenario: Series present only in the main catalog
- **WHEN** a series appears in the main series catalog but has no matching per-series detail data
- **THEN** `ListSeries` and `GetSeries` still return that series, with `known_card_names` empty

#### Scenario: Series present only in detail data
- **WHEN** a series appears only in per-series detail data and not in the main series catalog
- **THEN** `ListSeries` and `GetSeries` still return that series, using year `0` and empty `special_features`/`special_editions` unless the detail data's metadata supplies a year
