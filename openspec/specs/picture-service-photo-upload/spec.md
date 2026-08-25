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
`UploadPhoto` SHALL assign the uploaded photo a generated identifier (not derived from the original file name) and persist its bytes in the durable object store under that identifier, consistent with `picture-service-photo-storage`'s stable-identity requirement.

#### Scenario: Two uploads share an original file name
- **WHEN** two photos are uploaded via `UploadPhoto` whose original file names are identical
- **THEN** both are assigned distinct generated identifiers and are both stored and retrievable independently

### Requirement: Upload triggers analysis on completion
`UploadPhoto` SHALL trigger AI analysis of the uploaded photo once its byte stream has been fully received and stored, without requiring a separate call to start analysis.

#### Scenario: Analysis begins after a successful upload
- **WHEN** `UploadPhoto`'s client stream completes and the photo has been stored successfully
- **THEN** PictureService begins AI analysis of that photo before returning a response to the caller
