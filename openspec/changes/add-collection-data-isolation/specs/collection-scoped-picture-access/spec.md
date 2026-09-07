## Purpose

Defines the shared contract between `NinjagoScanner.Web` and `NinjagoScanner.PictureService` for scoping every photo/sidecar operation to one collection, including the trust boundary: Web decides who is authorized, PictureService only guarantees isolation between collections.

## ADDED Requirements

### Requirement: Every collection-scoped RPC requires a collection_id
PictureService SHALL require a `collection_id` on every RPC that reads or writes photo or sidecar data, and SHALL reject a call whose `collection_id` is missing or empty with an `InvalidArgument` error.

#### Scenario: Missing collection_id
- **WHEN** a collection-scoped RPC is called with no `collection_id` or an empty `collection_id`
- **THEN** the call fails with `InvalidArgument` and no photo or sidecar data is read or written

#### Scenario: Well-formed collection_id
- **WHEN** a collection-scoped RPC is called with a non-empty `collection_id`
- **THEN** the call proceeds to PictureService's normal handling for that RPC

### Requirement: Data isolation between collections
PictureService SHALL NOT return or modify photo or sidecar data belonging to a collection other than the one identified by the request's `collection_id`, even when another collection contains data with the same photo identifier or file name.

#### Scenario: Listing one collection never returns another's photos
- **WHEN** `ListCards` (or any other listing/reporting RPC) is called with a given `collection_id`, and a different collection also has stored photos
- **THEN** the response includes only photos belonging to the given `collection_id`

#### Scenario: Operating on a photo identifier that exists only in a different collection
- **WHEN** an RPC that reads, updates, downloads, or deletes a single photo is called with a `collection_id` and a photo identifier that is only stored under a different collection
- **THEN** the RPC treats the photo as not found within the given `collection_id`, rather than acting on the other collection's data

### Requirement: PictureService validates collection existence, not caller authorization
PictureService SHALL verify that a given `collection_id` refers to a collection that Web has previously created, returning a not-found error if it does not. PictureService SHALL NOT independently verify that the calling user is authorized for that collection — Web is solely responsible for that decision, since PictureService has no public network endpoint and Web is its only caller.

#### Scenario: Unknown collection_id
- **WHEN** a collection-scoped RPC is called with a `collection_id` that does not correspond to any collection PictureService has recorded
- **THEN** the call fails with a not-found error

#### Scenario: PictureService performs no membership check
- **WHEN** a collection-scoped RPC is called with a `collection_id` that does exist
- **THEN** PictureService does not query or evaluate which user, role, or membership authorized the call — it proceeds based on the collection's existence alone

### Requirement: Web resolves and supplies the caller's authorized collection_id
Web SHALL resolve the acting user's authorized collection (per `web-collections`) before calling any collection-scoped PictureService RPC, and SHALL supply that `collection_id` on the call.

#### Scenario: Page load passes the resolved collection on every backend call
- **WHEN** a page in Web makes one or more calls to a collection-scoped PictureService RPC while serving a single request
- **THEN** every such call carries the `collection_id` that Web resolved as authorized for the acting user on that request
