## REMOVED Requirements

### Requirement: Uploaded file is saved under a sanitized, collision-safe name
**Reason**: Photos are stored in S3 keyed by a generated photo ID (see `picture-service-photo-storage`'s "Stable photo identity" requirement), not by a derived local file name. This requirement described local-filesystem behavior that already didn't match PictureService's S3-backed storage before this change — `cloud-hosting-migration` removed the `UploadPhoto` RPC this requirement depended on without updating this spec; this change corrects the drift while reviving the RPC.
**Migration**: Superseded by "Uploaded photo is assigned a generated identifier" below.

### Requirement: Upload target directory is created if missing
**Reason**: There is no local target directory — uploads are written to the S3 photo bucket, which requires no directory-creation step.
**Migration**: None; not applicable to object storage.

## MODIFIED Requirements

### Requirement: Upload is a metadata-then-bytes stream
`UploadPhoto` SHALL accept a client stream whose first message carries upload metadata (original file name) and whose subsequent messages carry the raw file bytes, which are concatenated in order.

#### Scenario: Well-formed upload stream
- **WHEN** a client sends one metadata message followed by one or more byte-chunk messages
- **THEN** the server reconstructs the full file content by concatenating the chunk messages in the order received

## ADDED Requirements

### Requirement: Uploaded photo is assigned a generated identifier and stored durably
`UploadPhoto` SHALL assign the uploaded photo a generated identifier (not derived from the original file name) and persist its bytes in the durable object store under that identifier, consistent with `picture-service-photo-storage`'s stable-identity requirement.

#### Scenario: Two uploads share an original file name
- **WHEN** two photos are uploaded via `UploadPhoto` whose original file names are identical
- **THEN** both are assigned distinct generated identifiers and are both stored and retrievable independently

### Requirement: Upload triggers analysis on completion
`UploadPhoto` SHALL trigger AI analysis of the uploaded photo once its byte stream has been fully received and stored, without requiring a separate call to start analysis.

#### Scenario: Analysis begins after a successful upload
- **WHEN** `UploadPhoto`'s client stream completes and the photo has been stored successfully
- **THEN** PictureService begins AI analysis of that photo before returning a response to the caller
