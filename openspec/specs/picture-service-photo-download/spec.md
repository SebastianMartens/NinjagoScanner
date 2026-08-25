# picture-service-photo-download Specification

## Purpose

Lets PictureService resolve one photo ID, or many at once, to short-lived pre-signed S3 download URLs, so callers displaying many photos on one page don't need one request per photo.

## Requirements

### Requirement: Batched resolution of photo download URLs
PictureService SHALL expose a way to resolve a list of photo IDs to their pre-signed download URLs in a single request-response round trip, in addition to the existing single-photo `GetPhotoDownloadUrl`. The response SHALL include a download URL for every requested photo ID that currently has stored photo bytes.

#### Scenario: Resolving many photos in one request
- **WHEN** a caller requests download URLs for a list of photo IDs that all currently exist
- **THEN** a single response is returned containing one pre-signed download URL per requested photo ID

#### Scenario: Requesting an empty list
- **WHEN** a caller requests download URLs for an empty list of photo IDs
- **THEN** a response with no download URLs is returned, without error

### Requirement: Unknown photo IDs do not fail the whole batch
If a batch request includes a photo ID that has no stored photo bytes (for example because the photo was deleted after the caller listed it), PictureService SHALL still return download URLs for every other requested photo ID that does exist, rather than failing the entire request.

#### Scenario: One photo ID in the batch no longer exists
- **WHEN** a caller requests download URLs for a list of photo IDs where one ID has no stored photo bytes and the rest do
- **THEN** the response contains download URLs for every photo ID that exists, and no download URL for the missing one, and the call does not fail
