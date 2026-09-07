## MODIFIED Requirements

### Requirement: DeletePhoto removes the image file and its sidecar file
`DeletePhoto` SHALL delete the image file identified by the given image file name within the given `collection_id` from the resolved `cardFotos` directory, and SHALL also delete that image's sidecar file if one exists. The absence of a sidecar file SHALL NOT be treated as an error.

#### Scenario: Deleting a scanned photo with a sidecar
- **WHEN** `DeletePhoto` is called with a `collection_id` and an image file name that has both an image file and a sidecar file within that collection
- **THEN** both the image file and its sidecar file are removed from disk

#### Scenario: Deleting a photo with no sidecar yet
- **WHEN** `DeletePhoto` is called with a `collection_id` and an image file name that exists within that collection but has no sidecar file
- **THEN** the image file is removed from disk and the call succeeds without error

### Requirement: DeletePhoto fails for an image that does not exist
`DeletePhoto` SHALL return a not-found error and SHALL NOT delete any file when the given image file name does not exist within the given `collection_id` in the resolved `cardFotos` directory — including when that image file name exists only in a different collection.

#### Scenario: Deleting a nonexistent image
- **WHEN** `DeletePhoto` is called with a `collection_id` and an image file name that does not exist in the resolved directory
- **THEN** the call fails with a not-found error and no file on disk is changed

#### Scenario: Deleting an image that exists only in a different collection
- **WHEN** `DeletePhoto` is called with a `collection_id` and an image file name that exists only under a different collection
- **THEN** the call fails with a not-found error and no file on disk is changed
