## Purpose

Lets PictureService keep photos and their analysis/review metadata durable and independently accessible, without depending on any single compute instance's local disk.

## ADDED Requirements

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
The system SHALL persist sidecar metadata (analysis status, review status, card match, Gemini output, and related fields) for each photo in a durable record store, keyed by the photo's generated identifier.

#### Scenario: Sidecar update persists across restarts
- **WHEN** a sidecar field (e.g. Review Status) is updated for a photo and PictureService is subsequently restarted
- **THEN** reading that photo's sidecar afterward reflects the update

### Requirement: One-time migration preserves local originals
The system SHALL provide a one-time migration process that copies existing local photo and sidecar data into the new storage backend without deleting or modifying the local originals.

#### Scenario: Migration preserves local files
- **WHEN** the migration process completes successfully for an existing local photo and its sidecar
- **THEN** the original local files remain unchanged on disk, and the same photo and sidecar data are also retrievable from the new storage backend
