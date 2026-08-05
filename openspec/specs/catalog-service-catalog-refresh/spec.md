# catalog-service-catalog-refresh Specification

## Purpose

Keeps served catalog data consistent with the on-disk source files without requiring a service restart, while avoiding unnecessary reloads on every request.

## Requirements

### Requirement: Cached snapshot reused when data is unchanged
The system SHALL reuse the cached in-memory catalog snapshot to serve a request when the most recent last-write timestamp among the JSON files directly in the data directory is unchanged since the previous load.

#### Scenario: No files changed since last load
- **WHEN** a catalog request is served and no JSON file in the data directory has a last-write timestamp different from the one recorded at the previous load
- **THEN** the system returns the existing cached snapshot without re-reading files from disk

### Requirement: Snapshot invalidated when source data changes
The system SHALL rebuild the catalog snapshot from disk the next time it is queried after the most recent last-write timestamp among the JSON files directly in the data directory changes from the timestamp recorded for the cached snapshot (for example because a file was added or modified).

#### Scenario: A detail file is modified after the last load
- **WHEN** a file in the data directory (for example a per-series detail file) is modified after the last successful load, changing the most recent last-write timestamp among the directory's JSON files
- **THEN** the next catalog request rebuilds the snapshot from disk and reflects the updated content, without the service being restarted

### Requirement: Load failures degrade to an empty snapshot
If loading the catalog from disk fails for any reason, the system SHALL fall back to an empty snapshot (no series, no cards) rather than raising an error to the caller, while still reporting the resolved data directory and a load timestamp.

#### Scenario: Main catalog file is missing or malformed
- **WHEN** the main catalog file cannot be read or parsed
- **THEN** the resulting snapshot has empty series and card lists, and `GetServiceInfo` still returns the resolved `data_directory` and a `loaded_at_utc` timestamp
