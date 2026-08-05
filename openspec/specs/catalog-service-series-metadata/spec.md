# catalog-service-series-metadata Specification

## Purpose

Exposes descriptive metadata (year, logo, theme, highlights) for a series, distinct from the structural series entry, to support richer catalog presentation.

## Requirements

### Requirement: Look up series metadata by name
`GetSeriesMetadata` SHALL return the metadata associated with a series when it exists, using the same normalized name lookup as series lookup (case-insensitive, ignoring underscores, hyphens, and repeated whitespace), and SHALL indicate when no metadata is found.

#### Scenario: Metadata found
- **WHEN** a client calls `GetSeriesMetadata` with a `series_name` matching a series that has detail data
- **THEN** the response has `found = true` and `metadata` populated with that series' `series_name`, `year`, `logo`, `theme`, and `highlights`

#### Scenario: Metadata not found
- **WHEN** a client calls `GetSeriesMetadata` with a `series_name` for which no series detail data exists
- **THEN** the response has `found = false` and `metadata` left unset

### Requirement: Missing metadata fields default rather than error
When a metadata field (year, logo, or theme) is absent from the underlying detail data, the response SHALL substitute that field's zero-value default (year `0`, empty string) instead of failing the request.

#### Scenario: Detail data omits optional fields
- **WHEN** a series' detail data omits its logo, theme, or year field
- **THEN** `GetSeriesMetadata` still returns `found = true`, with the omitted fields set to their zero-value defaults and all other fields populated normally
