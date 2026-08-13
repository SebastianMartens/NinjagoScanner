# picture-service-photo-deletion Specification

## Purpose

Lets a caller permanently remove a scanned card photo and its sidecar file from disk in one RPC call, and keeps PictureService's in-memory sidecar cache consistent with that removal.

## Requirements

### Requirement: DeletePhoto removes the image file and its sidecar file
`DeletePhoto` SHALL delete the image file identified by the given image file name from the resolved `cardFotos` directory, and SHALL also delete that image's sidecar file if one exists. The absence of a sidecar file SHALL NOT be treated as an error.

#### Scenario: Deleting a scanned photo with a sidecar
- **WHEN** `DeletePhoto` is called for an image file name that has both an image file and a sidecar file
- **THEN** both the image file and its sidecar file are removed from disk

#### Scenario: Deleting a photo with no sidecar yet
- **WHEN** `DeletePhoto` is called for an image file name that exists but has no sidecar file
- **THEN** the image file is removed from disk and the call succeeds without error

### Requirement: DeletePhoto fails for an image that does not exist
`DeletePhoto` SHALL return a not-found error and SHALL NOT delete any file when the given image file name does not exist in the resolved `cardFotos` directory.

#### Scenario: Deleting a nonexistent image
- **WHEN** `DeletePhoto` is called for an image file name that does not exist in the resolved directory
- **THEN** the call fails with a not-found error and no file on disk is changed

### Requirement: DeletePhoto evicts the deleted photo from the sidecar cache
After a successful deletion, `DeletePhoto` SHALL remove any cached sidecar entry for that image's sidecar path, so subsequent reads do not return stale data for a file that no longer exists.

#### Scenario: Listing cards after deletion does not return the deleted photo's cached sidecar
- **WHEN** a photo's sidecar was previously read (and cached) and that photo is then deleted via `DeletePhoto`
- **THEN** a subsequent `ListCards` call does not include an entry for the deleted image
