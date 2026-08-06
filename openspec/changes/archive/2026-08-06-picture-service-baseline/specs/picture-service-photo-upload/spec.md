## Purpose

Defines the client-streaming `UploadPhoto` RPC used to add a new card photo (e.g. taken on a mobile device) into the card photos directory.

## ADDED Requirements

### Requirement: Upload is a metadata-then-bytes stream
`UploadPhoto` SHALL accept a client stream whose first message carries upload metadata (original file name and an optional card photos directory override) and whose subsequent messages carry the raw file bytes, which are concatenated in order.

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

### Requirement: Uploaded file is saved under a sanitized, collision-safe name
`UploadPhoto` SHALL derive the stored file name by stripping characters other than letters, digits, `_`, and `-` from the original file name's stem (falling back to `mobile-photo` if nothing remains), prefixing it with a UTC timestamp, and appending a numeric suffix if the resulting name already exists in the target directory, retrying up to 100 candidate names before failing.

#### Scenario: File name with unsafe characters
- **WHEN** the original file name contains characters outside letters, digits, `_`, and `-`
- **THEN** those characters are replaced/stripped in the stored file name, which is still prefixed with a UTC timestamp

#### Scenario: File name with no usable characters
- **WHEN** the original file name's stem contains no letters, digits, `_`, or `-` characters
- **THEN** the stored file name uses `mobile-photo` as its sanitized stem

#### Scenario: Name collision
- **WHEN** the sanitized, timestamp-prefixed candidate file name already exists in the target directory
- **THEN** the server retries with a numeric suffix appended, up to 100 attempts, before giving up

#### Scenario: All candidate names exhausted
- **WHEN** all 100 candidate names for an upload already exist in the target directory
- **THEN** the call fails with an `Internal` error

### Requirement: Upload target directory is created if missing
`UploadPhoto` SHALL create the resolved card photos directory (including the optional per-request override) if it does not already exist, before writing the uploaded file.

#### Scenario: Directory does not exist yet
- **WHEN** the resolved target directory for the upload does not exist on disk
- **THEN** the directory is created before the file is written, and the upload succeeds
