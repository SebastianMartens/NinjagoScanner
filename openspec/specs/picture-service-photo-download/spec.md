# picture-service-photo-download Specification

## Purpose

Lets PictureService resolve one photo ID, or many at once, to short-lived pre-signed S3 download URLs, so callers displaying many photos on one page don't need one request per photo.

## Requirements

### Requirement: Resolution of a single photo's download URL
PictureService SHALL expose a way to resolve one photo ID to its short-lived pre-signed download URL, returning an error if no photo is stored under that ID. This is PictureService's only remaining direct way to resolve a download URL outside of `ListCards` (see `picture-service-card-listing`), used when a caller has a single photo ID in hand without having just listed it — such as immediately after uploading a new photo.

#### Scenario: Resolving an existing photo's download URL
- **WHEN** a caller requests the download URL for a photo ID that currently has stored photo bytes
- **THEN** a pre-signed download URL for that photo is returned

#### Scenario: Requesting a download URL for a photo that does not exist
- **WHEN** a caller requests the download URL for a photo ID that has no stored photo bytes
- **THEN** the request fails with a not-found error instead of returning a URL
