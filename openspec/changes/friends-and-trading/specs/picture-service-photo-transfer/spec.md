## Purpose

Defines the PictureService RPC that moves card photos and their sidecars between two collections, used only by trade execution.

## ADDED Requirements

### Requirement: TransferPhotos moves photos between collections
PictureService SHALL expose `TransferPhotos` taking a transfer ID and a list of moves, each naming a source `collection_id`, a destination `collection_id` and a photo ID (so both directions of a trade are one call). For each move it SHALL copy the stored bytes to the destination under a newly generated photo ID, write the sidecar there with all fields preserved, and then remove the source photo and sidecar. The response SHALL map each old photo ID to its new ID.

#### Scenario: Successful move
- **WHEN** TransferPhotos is called with valid collections and 2 photos
- **THEN** the destination lists 2 new photos with identical sidecar content, the source no longer lists them, and the response maps old to new IDs

#### Scenario: Unknown photo
- **WHEN** a listed photo does not exist in the source collection
- **THEN** the call fails with not-found and nothing is moved

#### Scenario: Same collection
- **WHEN** a move's source and destination are equal
- **THEN** the call fails with InvalidArgument

#### Scenario: Unknown collection
- **WHEN** either collection ID is unknown or empty
- **THEN** the call fails (not-found / InvalidArgument) and nothing is moved

### Requirement: All-or-nothing with compensation
If any step fails, PictureService SHALL remove anything it already created in the destination and SHALL leave the source untouched, so no photo is lost or exists in both collections. Source deletion SHALL only occur after all destination writes succeeded.

#### Scenario: Failure mid-transfer
- **WHEN** copying the second of three photos fails
- **THEN** the first photo's destination copy is removed and all three remain in the source

#### Scenario: Idempotent retry
- **WHEN** a call is repeated with the same transfer ID after success
- **THEN** it returns the original mapping without moving anything again

### Requirement: Caches stay consistent
After a transfer, sidecar reads for both collections SHALL reflect the new state immediately.

#### Scenario: Read after transfer
- **WHEN** ListCards is called for both collections after a transfer
- **THEN** the moved cards appear only in the destination
