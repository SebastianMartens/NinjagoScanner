# picture-service-photo-download Specification

## Purpose

Lets PictureService resolve one photo ID, or many at once, to short-lived pre-signed S3 download URLs, so callers displaying many photos on one page don't need one request per photo.

## Requirements

### Requirement: Resolution of a single photo's download URL
PictureService SHALL expose a way to resolve one photo ID within a given `collection_id` to its short-lived pre-signed download URL, returning a not-found error if no photo is stored under that ID within that collection — including when the photo ID exists only in a different collection. This is for a caller that has a single photo ID in hand — such as immediately after uploading a new photo. `ListCards` (see `picture-service-card-listing`) returns no download URLs, so a caller that needs URLs for several photos uses the bounded many-photos resolution below instead of calling this once per photo.

#### Scenario: Resolving an existing photo's download URL
- **WHEN** a caller requests the download URL for a photo ID and `collection_id` that currently has stored photo bytes in that collection
- **THEN** a pre-signed download URL for that photo is returned

#### Scenario: Requesting a download URL for a photo that does not exist
- **WHEN** a caller requests the download URL for a photo ID that has no stored photo bytes in the given collection
- **THEN** the request fails with a not-found error instead of returning a URL

#### Scenario: Requesting a download URL for a photo that exists only in a different collection
- **WHEN** a caller requests the download URL for a photo ID and a `collection_id`, and that photo ID's stored bytes belong to a different collection
- **THEN** the request fails with a not-found error instead of returning a URL

### Requirement: Resolution of many photos' download URLs at once, bounded to a caller-supplied list
PictureService SHALL expose a way to resolve a caller-supplied list of photo IDs within a given `collection_id` to their short-lived pre-signed download URLs in one request, so a caller that only needs URLs for a bounded subset of a collection's photos (such as the photos currently displayed on a page) does not need to resolve them one at a time, and does not receive URLs for photos it did not ask about. A requested photo ID with no stored photo bytes in that collection — including a stale ID for a photo since deleted, or an ID that exists only in a different collection — SHALL be omitted from the response rather than causing the whole request to fail.

#### Scenario: Resolving several photos' download URLs in one call
- **WHEN** a caller requests download URLs for a list of photo IDs that all have stored photo bytes in the given `collection_id`
- **THEN** the response includes a pre-signed download URL for each of them, resolved by a single request

#### Scenario: A stale or missing photo ID is omitted, not an error
- **WHEN** a caller requests download URLs for a list of photo IDs that includes one with no stored photo bytes in that collection
- **THEN** the response includes a pre-signed download URL for every other requested photo ID that does have stored bytes, and no entry for the one that does not, and the request does not fail

#### Scenario: An empty list resolves to an empty response
- **WHEN** a caller requests download URLs for an empty list of photo IDs
- **THEN** the response contains no entries, and no error is raised

#### Scenario: Only the requested photos are resolved
- **WHEN** a caller requests download URLs for a list of photo IDs
- **THEN** the response contains no entry for any photo in the collection that was not in the requested list, even if that photo exists and has stored bytes
