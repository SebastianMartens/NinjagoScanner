# web-photo-upload Specification

## Purpose
Lets a person add a new card photo — typically captured directly on a mobile device — into the shared storage backend, without performing any analysis as part of the upload itself.
## Requirements
### Requirement: The file picker favors capturing a new photo on mobile
The `/upload` page SHALL present a file input restricted to image files and hinting mobile browsers to open the environment-facing camera by default.

#### Scenario: Opening the upload page on a mobile browser
- **WHEN** a user opens `/upload` on a mobile device and taps the file field
- **THEN** the browser is hinted to offer the rear ("environment") camera alongside the regular file picker, and only image files can be selected

### Requirement: Upload streams through the app server
The system SHALL let the browser upload a photo by streaming its bytes to `NinjagoScanner.Web`, which forwards them to PictureService for storage, rather than the browser uploading directly to the storage backend.

#### Scenario: Successful upload
- **WHEN** the user selects a supported photo and confirms upload
- **THEN** the browser streams the photo bytes to the Web app, which streams them onward to PictureService, and the photo becomes available without the browser ever addressing the storage backend directly

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

### Requirement: Upload progress disables re-submission
While an upload is in progress, the upload button SHALL be disabled and show an in-progress label, and SHALL only be re-enabled once the upload completes or fails.

#### Scenario: Clicking upload while one is already running
- **WHEN** an upload is in progress
- **THEN** the upload button is disabled and labeled to indicate the upload is running, preventing a second concurrent upload
