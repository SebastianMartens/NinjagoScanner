## Purpose

Gives operators and client services a lightweight diagnostic view of the catalog service's current data source and load state.

## ADDED Requirements

### Requirement: Report current catalog load state
`GetServiceInfo` SHALL return the resolved data directory the catalog is loaded from, the number of series currently loaded, and the UTC timestamp of the last successful catalog load.

#### Scenario: Requesting service info
- **WHEN** a client calls `GetServiceInfo`
- **THEN** the response contains `data_directory` (the resolved absolute path the catalog was loaded from), `series_count` (matching the number of series in the current snapshot), and `loaded_at_utc` (an ISO-8601 UTC timestamp of the last load)
