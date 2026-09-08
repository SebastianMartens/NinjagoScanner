## ADDED Requirements

### Requirement: Cache entries are keyed by collection and photo together
The system SHALL key each in-memory cache entry by the combination of `collection_id` and photo identifier, so that a read scoped to one collection is never served from another collection's cached entry, even if the same photo identifier exists in both.

#### Scenario: Same photo identifier in two collections is cached independently
- **WHEN** sidecar data is read for the same photo identifier in two different collections
- **THEN** each collection's read is served from (and populates) its own cache entry, and neither reflects the other collection's data
