## Purpose

Lets a caller learn which source file names a collection's photos were uploaded under, cheaply and without fetching any photo data, so it can avoid uploading a file that is already present.

## ADDED Requirements

### Requirement: Source file names of a collection can be listed
PictureService SHALL provide an RPC that, given a `collection_id`, returns the source file name recorded for each photo in that collection. Photos whose sidecar records no source file name SHALL NOT contribute an entry. The response SHALL contain only file names — no photo IDs, download URLs, analysis data, or review data.

#### Scenario: Collection with named photos
- **WHEN** the RPC is called for a collection containing photos uploaded as `a.jpg`, `b.png` and `c.webp`
- **THEN** the response contains the file names `a.jpg`, `b.png` and `c.webp`

#### Scenario: Photo without a recorded file name
- **WHEN** a photo in the collection has a sidecar with no source file name
- **THEN** the response contains no entry for that photo, and the call still succeeds

#### Scenario: Empty collection
- **WHEN** the RPC is called for a collection that has no photos
- **THEN** the response contains an empty list of file names

### Requirement: Duplicate file names are reported once
When several photos in the collection share the same source file name, the response SHALL contain that file name exactly once, so a caller can treat the result as a set.

#### Scenario: Two photos share a file name
- **WHEN** two photos in the collection were both uploaded as `IMG_0001.jpg`
- **THEN** the response lists `IMG_0001.jpg` once

### Requirement: Listing is scoped to the requested collection
The RPC SHALL require a `collection_id`, rejecting the call with `InvalidArgument` if it is missing or blank, and SHALL return only file names of photos belonging to that collection.

#### Scenario: Missing collection identifier
- **WHEN** the RPC is called with an empty `collection_id`
- **THEN** the call fails with `InvalidArgument`

#### Scenario: Another collection's photos are excluded
- **WHEN** the RPC is called for one collection while a different collection also has photos
- **THEN** the response contains none of the other collection's file names

### Requirement: Listing cost does not grow per photo
The RPC SHALL read the collection's file names using a bounded number of paginated bulk reads of the sidecar store, and SHALL NOT issue a per-photo storage request, create download URLs, or list photo objects.

#### Scenario: Collection with thousands of photos
- **WHEN** the RPC is called for a collection containing several thousand photos
- **THEN** the file names are read with paginated bulk queries rather than one request per photo, and no download URL is created
