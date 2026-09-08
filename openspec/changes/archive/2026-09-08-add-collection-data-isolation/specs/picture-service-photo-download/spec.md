## MODIFIED Requirements

### Requirement: Resolution of a single photo's download URL
PictureService SHALL expose a way to resolve one photo ID within a given `collection_id` to its short-lived pre-signed download URL, returning a not-found error if no photo is stored under that ID within that collection — including when the photo ID exists only in a different collection. This is PictureService's only remaining direct way to resolve a download URL outside of `ListCards` (see `picture-service-card-listing`), used when a caller has a single photo ID in hand without having just listed it — such as immediately after uploading a new photo.

#### Scenario: Resolving an existing photo's download URL
- **WHEN** a caller requests the download URL for a photo ID and `collection_id` that currently has stored photo bytes in that collection
- **THEN** a pre-signed download URL for that photo is returned

#### Scenario: Requesting a download URL for a photo that does not exist
- **WHEN** a caller requests the download URL for a photo ID that has no stored photo bytes in the given collection
- **THEN** the request fails with a not-found error instead of returning a URL

#### Scenario: Requesting a download URL for a photo that exists only in a different collection
- **WHEN** a caller requests the download URL for a photo ID and a `collection_id`, and that photo ID's stored bytes belong to a different collection
- **THEN** the request fails with a not-found error instead of returning a URL
