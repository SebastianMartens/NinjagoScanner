## ADDED Requirements

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
