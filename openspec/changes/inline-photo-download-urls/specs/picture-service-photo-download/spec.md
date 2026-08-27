## REMOVED Requirements

### Requirement: Batched resolution of photo download URLs
**Reason**: `ListCards` (see `picture-service-card-listing`) now includes a download URL directly on every `CardEntry` it returns, so callers no longer need a separate call to resolve many photo IDs to download URLs at once. Keeping the batch RPC alive as an unused, redundant path was itself a source of the slowness this capability existed to fix — it re-checked existence for photos `ListCards` had just confirmed exist, one photo at a time.
**Migration**: Callers that resolved many photo IDs to download URLs after calling `ListCards` should read the `download_url` already present on each `CardEntry` instead of making a follow-up call. There is no direct RPC replacement.

### Requirement: Unknown photo IDs do not fail the whole batch
**Reason**: This requirement only existed to describe the batch RPC's error handling. It is removed along with the batch RPC itself; see the removed "Batched resolution of photo download URLs" requirement.
**Migration**: Not applicable — no replacement RPC. `ListCards` only ever returns entries for photos it found while listing, so there is no equivalent "unknown ID in a requested batch" case to handle.

## ADDED Requirements

### Requirement: Resolution of a single photo's download URL
PictureService SHALL expose a way to resolve one photo ID to its short-lived pre-signed download URL, returning an error if no photo is stored under that ID. This is PictureService's only remaining direct way to resolve a download URL outside of `ListCards` (see `picture-service-card-listing`), used when a caller has a single photo ID in hand without having just listed it — such as immediately after uploading a new photo.

#### Scenario: Resolving an existing photo's download URL
- **WHEN** a caller requests the download URL for a photo ID that currently has stored photo bytes
- **THEN** a pre-signed download URL for that photo is returned

#### Scenario: Requesting a download URL for a photo that does not exist
- **WHEN** a caller requests the download URL for a photo ID that has no stored photo bytes
- **THEN** the request fails with a not-found error instead of returning a URL
