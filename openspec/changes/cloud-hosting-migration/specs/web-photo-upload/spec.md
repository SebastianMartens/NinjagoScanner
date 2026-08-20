## Purpose

Defines how a photo gets from the user's browser into storage once Web no longer holds a persistent server-side connection to stream it through.

## ADDED Requirements

### Requirement: Direct-to-storage upload
The system SHALL let the browser upload a photo directly to the storage backend using a short-lived, pre-authorized upload URL obtained from the BFF, rather than transmitting the photo bytes through the BFF itself.

#### Scenario: Successful direct upload
- **WHEN** the user selects a supported photo and confirms upload
- **THEN** the client obtains a pre-authorized upload URL from the BFF and uploads the photo bytes directly to storage without those bytes passing through the BFF

### Requirement: File type and size validation before upload
The system SHALL reject photos that are not one of the supported image types (JPG, PNG, BMP, WEBP) or that exceed the configured maximum upload size, before issuing an upload URL.

#### Scenario: Oversized file rejected
- **WHEN** the user selects a photo larger than the configured maximum upload size
- **THEN** the BFF does not issue an upload URL, and the client displays an error instead of uploading

#### Scenario: Unsupported file type rejected
- **WHEN** the user selects a file whose type is not one of the supported image types
- **THEN** the BFF does not issue an upload URL, and the client displays an error instead of uploading

### Requirement: Analysis starts only after upload confirmation
The system SHALL trigger photo analysis only after the browser confirms the direct upload to storage has completed.

#### Scenario: Analysis starts after upload confirmation
- **WHEN** the browser finishes uploading a photo directly to storage and notifies the BFF
- **THEN** PictureService begins analyzing that photo
