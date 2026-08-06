# web-photo-upload Specification

## Purpose

Lets a person add a new card photo — typically captured directly on a mobile device — into the shared `cardFotos` storage via PictureService, without performing any analysis as part of the upload.

## ADDED Requirements

### Requirement: The file picker favors capturing a new photo on mobile
The `/upload` page SHALL present a file input restricted to image files and hinting mobile browsers to open the environment-facing camera by default.

#### Scenario: Opening the upload page on a mobile browser
- **WHEN** a user opens `/upload` on a mobile device and taps the file field
- **THEN** the browser is hinted to offer the rear ("environment") camera alongside the regular file picker, and only image files can be selected

### Requirement: Selected file is validated before upload
The system SHALL reject an upload attempt with a user-facing error message, without contacting PictureService, when no file is selected, when the selected file is empty, when the selected file exceeds the configured maximum upload size, or when the selected file's extension is not one of `.jpg`, `.jpeg`, `.png`, `.bmp`, `.webp`.

#### Scenario: No file selected
- **WHEN** a user clicks "Foto hochladen" without selecting a file
- **THEN** an error message asks them to select a photo first, and no upload is attempted

#### Scenario: File exceeds the maximum size
- **WHEN** a user selects a file larger than the configured maximum upload size
- **THEN** the upload is rejected with an error message stating the maximum allowed size in MB

#### Scenario: Unsupported file type
- **WHEN** a user selects a file whose extension is not jpg, jpeg, png, bmp, or webp
- **THEN** the upload is rejected with an error message listing the supported types

### Requirement: Valid files are streamed to PictureService in chunks
A valid file SHALL be uploaded to PictureService over a client-streaming gRPC call, sent as an initial metadata message (original file name, target card photos directory) followed by the file content split into fixed-size chunks, and on success the page SHALL show the stored image file name returned by PictureService.

#### Scenario: Successful upload
- **WHEN** a valid file is uploaded and PictureService accepts it
- **THEN** the page shows a success message containing the stored file name and clears the file selection

#### Scenario: PictureService rejects the upload
- **WHEN** PictureService returns an `InvalidArgument` error for the streamed upload
- **THEN** the page shows PictureService's error detail as the error message instead of a generic failure

### Requirement: Upload progress disables re-submission
While an upload is in progress, the upload button SHALL be disabled and show an in-progress label, and SHALL only be re-enabled once the upload completes or fails.

#### Scenario: Clicking upload while one is already running
- **WHEN** an upload is in progress
- **THEN** the upload button is disabled and labeled to indicate the upload is running, preventing a second concurrent upload
