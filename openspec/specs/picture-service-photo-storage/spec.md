# picture-service-photo-storage Specification

## Purpose
Lets PictureService keep photos and their analysis/review metadata durable and independently accessible, without depending on any single compute instance's local disk.

## Requirements

### Requirement: Photo persistence independent of compute instance
The system SHALL persist uploaded photos in a durable object store, independent of any single compute instance, so photos survive service redeployment or restart.

#### Scenario: Photo survives redeployment
- **WHEN** a photo has been successfully uploaded and PictureService is subsequently redeployed or restarted
- **THEN** the photo remains retrievable afterward without data loss

### Requirement: Stable photo identity
The system SHALL assign each photo a generated identifier at upload time and SHALL NOT rely on the original filename as a unique identifier.

#### Scenario: Two uploads share an original filename
- **WHEN** two photos are uploaded whose original filenames are identical
- **THEN** both photos are stored and retrievable independently, without one overwriting the other

### Requirement: Sidecar metadata persistence
The system SHALL persist sidecar metadata (analysis status, review status, card match, Gemini output, and related fields) for each photo in a durable record store, keyed by the combination of the photo's collection and its generated identifier.

#### Scenario: Sidecar update persists across restarts
- **WHEN** a sidecar field (e.g. Review Status) is updated for a photo and PictureService is subsequently restarted
- **THEN** reading that photo's sidecar afterward, within the same collection, reflects the update

### Requirement: One-time migration preserves local originals
The system SHALL provide a one-time migration process that copies existing local photo and sidecar data into the new storage backend without deleting or modifying the local originals.

#### Scenario: Migration preserves local files
- **WHEN** the migration process completes successfully for an existing local photo and its sidecar
- **THEN** the original local files remain unchanged on disk, and the same photo and sidecar data are also retrievable from the new storage backend

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
