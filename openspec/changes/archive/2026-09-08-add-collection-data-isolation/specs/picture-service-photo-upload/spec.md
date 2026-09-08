## MODIFIED Requirements

### Requirement: Uploaded photo is assigned a generated identifier and stored durably
`UploadPhoto` SHALL assign the uploaded photo a generated identifier (not derived from the original file name) and persist its bytes in the durable object store under that identifier within the collection specified by the request's `collection_id`, consistent with `picture-service-photo-storage`'s stable-identity and collection-partitioning requirements.

#### Scenario: Two uploads share an original file name
- **WHEN** two photos are uploaded via `UploadPhoto` whose original file names are identical
- **THEN** both are assigned distinct generated identifiers and are both stored and retrievable independently

#### Scenario: Upload is stored within the requesting collection
- **WHEN** `UploadPhoto` completes successfully for a given `collection_id`
- **THEN** the resulting photo is retrievable via that same `collection_id`, and is not retrievable via any other collection's `collection_id`
