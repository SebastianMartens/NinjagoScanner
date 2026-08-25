## REMOVED Requirements

### Requirement: Direct-to-storage upload
**Reason**: The BFF that issued pre-authorized upload URLs is retired in this change. Upload reverts to streaming through the app server, matching the architecture from before `cloud-hosting-migration` — the server-hosted app server no longer has Lambda's payload-size/duration constraints that motivated moving bytes off it in the first place.
**Migration**: Superseded by the new "Upload streams through the app server" requirement below.

## ADDED Requirements

### Requirement: Upload streams through the app server
The system SHALL let the browser upload a photo by streaming its bytes to `NinjagoScanner.Web`, which forwards them to PictureService for storage, rather than the browser uploading directly to the storage backend.

#### Scenario: Successful upload
- **WHEN** the user selects a supported photo and confirms upload
- **THEN** the browser streams the photo bytes to the Web app, which streams them onward to PictureService, and the photo becomes available without the browser ever addressing the storage backend directly

## MODIFIED Requirements

### Requirement: File type and size validation before upload
The system SHALL reject photos that are not one of the supported image types (JPG, PNG, BMP, WEBP) or that exceed the configured maximum upload size, before the upload stream to PictureService begins.

#### Scenario: Oversized file rejected
- **WHEN** the user selects a photo larger than the configured maximum upload size
- **THEN** the Web app does not start streaming the file to PictureService, and the client displays an error instead of uploading

#### Scenario: Unsupported file type rejected
- **WHEN** the user selects a file whose type is not one of the supported image types
- **THEN** the Web app does not start streaming the file to PictureService, and the client displays an error instead of uploading

### Requirement: Analysis starts only after upload confirmation
The system SHALL trigger photo analysis only after the streamed upload to PictureService has completed successfully.

#### Scenario: Analysis starts after upload completes
- **WHEN** the Web app finishes streaming a photo's bytes to PictureService and PictureService confirms the upload succeeded
- **THEN** PictureService begins analyzing that photo
