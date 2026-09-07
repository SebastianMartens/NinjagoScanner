## ADDED Requirements

### Requirement: Photo storage is partitioned by collection
The system SHALL store each photo's bytes and sidecar record under both its collection and its generated identifier, so that photos and sidecars belonging to different collections are stored independently and never share a location, even if their generated identifiers happen to collide across collections.

#### Scenario: Two collections store data independently
- **WHEN** two different collections each have a stored photo
- **THEN** each photo's bytes and sidecar are stored and retrievable independently of the other collection's data

### Requirement: Existing pre-Collection data is migrated to a designated collection
The system SHALL provide a one-time migration that assigns every photo and sidecar stored before the Collection concept existed to a single designated collection, without altering the photo bytes or the sidecar's other fields.

#### Scenario: Migrating pre-Collection data
- **WHEN** the collection-assignment migration runs against the pre-existing (collection-less) photo and sidecar data
- **THEN** every such photo and sidecar becomes retrievable under the designated collection's `collection_id`, with its bytes and sidecar fields otherwise unchanged

## MODIFIED Requirements

### Requirement: Sidecar metadata persistence
The system SHALL persist sidecar metadata (analysis status, review status, card match, Gemini output, and related fields) for each photo in a durable record store, keyed by the combination of the photo's collection and its generated identifier.

#### Scenario: Sidecar update persists across restarts
- **WHEN** a sidecar field (e.g. Review Status) is updated for a photo and PictureService is subsequently restarted
- **THEN** reading that photo's sidecar afterward, within the same collection, reflects the update
