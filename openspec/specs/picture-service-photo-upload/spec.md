# picture-service-photo-upload Specification

## Purpose

Defines the client-streaming `UploadPhoto` RPC used to add a new card photo (e.g. taken on a mobile device) into the card photos directory.

## Requirements

### Requirement: Upload is a metadata-then-bytes stream
`UploadPhoto` SHALL accept a client stream whose first message carries upload metadata (original file name) and whose subsequent messages carry the raw file bytes, which are concatenated in order.

#### Scenario: Well-formed upload stream
- **WHEN** a client sends one metadata message followed by one or more byte-chunk messages
- **THEN** the server reconstructs the full file content by concatenating the chunk messages in the order received

### Requirement: Upload rejects invalid input
`UploadPhoto` SHALL reject the upload with an `InvalidArgument` error if no original file name was provided, if no file content was received, or if the file name's extension is not one of the supported image extensions (jpg, jpeg, png, bmp, webp).

#### Scenario: Missing file name
- **WHEN** the metadata message has no (or an empty) original file name
- **THEN** the call fails with `InvalidArgument`

#### Scenario: Empty file content
- **WHEN** the client sends metadata but no chunk data, or only empty chunks
- **THEN** the call fails with `InvalidArgument`

#### Scenario: Unsupported file extension
- **WHEN** the original file name's extension is not jpg, jpeg, png, bmp, or webp
- **THEN** the call fails with `InvalidArgument`

### Requirement: Uploaded photo is assigned a generated identifier and stored durably
`UploadPhoto` SHALL assign the uploaded photo a generated identifier (not derived from the original file name) and persist its bytes in the durable object store under that identifier within the collection specified by the request's `collection_id`, consistent with `picture-service-photo-storage`'s stable-identity and collection-partitioning requirements.

#### Scenario: Two uploads share an original file name
- **WHEN** two photos are uploaded via `UploadPhoto` whose original file names are identical
- **THEN** both are assigned distinct generated identifiers and are both stored and retrievable independently

#### Scenario: Upload is stored within the requesting collection
- **WHEN** `UploadPhoto` completes successfully for a given `collection_id`
- **THEN** the resulting photo is retrievable via that same `collection_id`, and is not retrievable via any other collection's `collection_id`

### Requirement: Upload triggers analysis on completion
`UploadPhoto` SHALL trigger AI analysis of the uploaded photo once its byte stream has been fully received and stored, without requiring a separate call to start analysis, UNLESS the upload's metadata sets `skip_analysis` (see "Upload can skip analysis").

#### Scenario: Analysis begins after a successful upload
- **WHEN** `UploadPhoto`'s client stream completes without `skip_analysis` set and the photo has been stored successfully
- **THEN** PictureService begins AI analysis of that photo before returning a response to the caller

### Requirement: Upload can skip analysis
When the upload metadata sets `skip_analysis`, `UploadPhoto` SHALL store the photo's bytes under a generated identifier, persist a sidecar for it recording the original file name and an analysis status of `notAnalyzed`, and return the resulting card entry without running AI analysis. All other upload validation (metadata-first stream, collection identifier, supported extension, non-empty content) SHALL still apply. A skip-analysis upload SHALL NOT require a Gemini API key or a reachable CatalogService.

#### Scenario: Skip-analysis upload stores the photo without analysis
- **WHEN** a client completes an upload stream whose metadata sets `skip_analysis`
- **THEN** the photo is stored, its sidecar records the original file name with analysis status `notAnalyzed`, the returned card entry reports `notAnalyzed`, and no AI analysis is run

#### Scenario: Skip-analysis upload works without Gemini or CatalogService
- **WHEN** a skip-analysis upload is made while no Gemini API key is configured, or CatalogService is unreachable
- **THEN** the upload succeeds instead of failing with `FailedPrecondition` or `Unavailable`

#### Scenario: Skipped-analysis photo is picked up by a later scan
- **WHEN** a photo was uploaded with `skip_analysis` and a bulk `Scan` later runs for its collection
- **THEN** the scan treats the photo as not yet analyzed, analyzes it, and preserves the recorded original file name

#### Scenario: Validation still applies
- **WHEN** a skip-analysis upload has an unsupported file extension or no content
- **THEN** the call fails with `InvalidArgument`, exactly as for an upload that does not skip analysis

### Requirement: Upload does not reject a file name that already exists
`UploadPhoto` SHALL NOT reject or skip an upload because a photo with the same original file name already exists in the collection, regardless of `skip_analysis`. Skipping already-uploaded file names is the caller's decision, made using the source file name listing.

#### Scenario: Skip-analysis upload with an existing file name
- **WHEN** a skip-analysis upload is made for a file name already present in the collection
- **THEN** the photo is stored under a new distinct generated identifier, consistent with "Uploaded photo is assigned a generated identifier and stored durably"
