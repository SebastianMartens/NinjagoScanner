## Purpose

Avoids re-reading and re-parsing sidecar files from disk on every request by keeping their contents in memory, while guaranteeing that reads always reflect the most recently written data.

## ADDED Requirements

### Requirement: Cached sidecar data is reused for unchanged files
Once a sidecar file's contents have been read during the service's process lifetime, the system SHALL serve subsequent reads of that same sidecar from an in-memory cache without re-reading the file from disk, as long as no write has occurred for that file since it was cached.

#### Scenario: Repeated reads of an unchanged sidecar
- **WHEN** sidecar data for an image is requested (for example via `ListCards`) more than once, with no write to that sidecar in between
- **THEN** every request after the first is served from the in-memory cache without reading the file from disk again

### Requirement: A sidecar not yet read is loaded from disk and cached
For an image whose sidecar file has not yet been read since the service started, the system SHALL read it from disk, parse it, and store the result in the in-memory cache so that later reads of the same file do not need to touch disk again.

#### Scenario: First read of a sidecar in the process lifetime
- **WHEN** a sidecar file is read for the first time since the service started
- **THEN** its content is read from disk and stored in the in-memory cache
- **AND** any later read of the same sidecar file is served from the cache

### Requirement: The cache always reflects the most recently written sidecar data
Whenever a sidecar is created or updated by any operation (photo scanning, a sidecar edit, a series reassignment, a review status change, or sidecar migration), the system SHALL update the in-memory cache with the newly written content as part of that operation, so that every read requested afterward returns that data without requiring a fresh disk read.

#### Scenario: Read immediately following a write
- **WHEN** a sidecar is written by any operation and its data is subsequently requested
- **THEN** the returned data matches what was just written

#### Scenario: Multiple writes to the same sidecar over time
- **WHEN** a sidecar is written more than once (for example an initial scan followed by a manual edit)
- **THEN** a read requested after the second write returns the second write's content, not the first
