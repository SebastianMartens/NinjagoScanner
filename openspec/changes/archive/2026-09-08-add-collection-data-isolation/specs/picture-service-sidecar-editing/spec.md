## ADDED Requirements

### Requirement: All sidecar-editing RPCs are scoped to a collection
`UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, and `UpdateReviewStatus` SHALL each require a `collection_id` identifying which collection the target image belongs to, SHALL create or update the sidecar record within that collection only, and SHALL NOT create, read, or modify a sidecar record belonging to a different collection, even if the same image file name exists there.

#### Scenario: Editing a sidecar creates it within the specified collection
- **WHEN** one of the sidecar-editing RPCs is called with a `collection_id` for an image with no existing sidecar in that collection
- **THEN** the new sidecar record is created within that collection

#### Scenario: Same file name in two collections is edited independently
- **WHEN** the same image file name exists in two different collections, and a sidecar-editing RPC is called with one collection's `collection_id`
- **THEN** only that collection's sidecar record is affected; the other collection's sidecar record (if any) for the same file name is left unchanged
