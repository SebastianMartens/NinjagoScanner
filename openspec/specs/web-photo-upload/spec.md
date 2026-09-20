# web-photo-upload Specification

## Purpose
Lets a person add a new card photo — typically captured directly on a mobile device — into the shared storage backend, without performing any analysis as part of the upload itself.
## Requirements
### Requirement: The file picker favors capturing a new photo on mobile
The `/upload` page SHALL present a single-photo file input restricted to image files and hinting mobile browsers to open the environment-facing camera by default. This input SHALL accept exactly one file, SHALL upload it and trigger analysis as described by the other requirements of this capability, and SHALL NOT offer multiple selection.

#### Scenario: Opening the upload page on a mobile browser
- **WHEN** a user opens `/upload` on a mobile device and taps the single-photo file field
- **THEN** the browser is hinted to offer the rear ("environment") camera alongside the regular file picker, only image files can be selected, and only one file can be chosen

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

### Requirement: Batch upload uses a separate multi-file input
The `/upload` page SHALL present, in addition to the single-photo input, a separate batch input that allows selecting multiple image files (up to 10,000 per selection) at once, optionally by selecting a folder, and that carries no camera-capture hint.

#### Scenario: Selecting many files
- **WHEN** a user selects 2,500 supported image files in the batch input
- **THEN** all 2,500 files are accepted as the selection for one batch upload

#### Scenario: Selection above the limit
- **WHEN** a user selects more than 10,000 files in the batch input
- **THEN** the page does not start uploading and shows an error stating the maximum number of files per batch

#### Scenario: No camera hint on the batch input
- **WHEN** a user on a mobile device taps the batch input
- **THEN** the browser is not hinted to open the camera

### Requirement: Batch upload never triggers analysis
Every photo uploaded through the batch input SHALL be uploaded without analysis, regardless of how many files were selected — including a selection of a single file. The single-photo input SHALL keep triggering analysis as before.

#### Scenario: Multiple files uploaded through the batch input
- **WHEN** a user uploads several files through the batch input
- **THEN** each photo is stored with analysis status `notAnalyzed` and none is analyzed as part of the upload

#### Scenario: One file uploaded through the batch input
- **WHEN** a user selects exactly one file in the batch input and uploads it
- **THEN** the photo is stored with analysis status `notAnalyzed` and is not analyzed as part of the upload

#### Scenario: Analysis is started later
- **WHEN** photos have been batch-uploaded
- **THEN** they can be analyzed afterwards through the Overview page's existing analysis action, with no additional step required on `/upload`

### Requirement: Batch files are uploaded one at a time
The batch upload SHALL upload the selected files sequentially, starting the next file only after the previous one has completed, skipped, or failed.

#### Scenario: Files are processed in order without overlap
- **WHEN** a batch of several files is uploaded
- **THEN** at most one file's upload is in progress at any moment

### Requirement: Batch upload skips files whose name already exists
Before uploading the first file of a batch, the system SHALL obtain the collection's existing source file names and SHALL skip, without uploading it, every selected file whose file name exactly equals (ordinal, case-sensitive) an existing name. A name uploaded successfully during the batch SHALL also count as existing for the remaining files of the same batch. Skipping SHALL NOT apply to the single-photo input.

#### Scenario: File name already in the collection
- **WHEN** a batch contains `IMG_0001.jpg` and the collection already has a photo whose source file name is `IMG_0001.jpg`
- **THEN** that file is not uploaded and is counted as skipped

#### Scenario: Same name twice within one batch
- **WHEN** a batch contains two files both named `IMG_0002.jpg` (for example from different folders) and no such name exists yet
- **THEN** the first is uploaded and the second is skipped

#### Scenario: Names differ only in case
- **WHEN** a batch contains `img_0003.jpg` and the collection has a photo named `IMG_0003.jpg`
- **THEN** the file is treated as new and uploaded

#### Scenario: Retrying an interrupted batch
- **WHEN** a batch of 1,000 files was interrupted after 400 files were uploaded and the user selects the same 1,000 files again
- **THEN** the 400 already-uploaded files are skipped and the remaining 600 are uploaded

#### Scenario: Single-photo upload ignores existing names
- **WHEN** a user uploads through the single-photo input a file whose name already exists in the collection
- **THEN** the photo is uploaded and analyzed as usual

### Requirement: Batch upload validates each file without aborting the batch
For each file in a batch, the system SHALL apply the supported-type and maximum-size checks of "File type and size validation before upload". A file that fails validation, or whose upload fails, SHALL be recorded as failed together with its name and reason, and the batch SHALL continue with the next file.

#### Scenario: Unsupported file among valid files
- **WHEN** a batch contains a text file among many image files
- **THEN** the text file is not uploaded and is recorded as failed with a reason, and the image files are still uploaded

#### Scenario: Oversized file among valid files
- **WHEN** a batch contains a file larger than the configured maximum upload size
- **THEN** that file is recorded as failed with a reason and the rest of the batch continues

#### Scenario: A single upload fails
- **WHEN** the upload of one file in a batch fails (for example because PictureService is temporarily unavailable)
- **THEN** that file is recorded as failed with the error message and the batch continues with the next file

### Requirement: Batch upload shows a progress summary
While and after a batch upload runs, the `/upload` page SHALL show the number of files processed out of the total selected, plus the number uploaded, skipped, and failed. It SHALL NOT render one row per selected file. When the batch has finished it SHALL list the names of skipped files and the names and reasons of failed files.

#### Scenario: Progress during a batch
- **WHEN** a batch of 10,000 files is being uploaded
- **THEN** the page displays counts such as "812 von 10.000 verarbeitet" with the uploaded, skipped and failed counts, and does not render an entry for each file

#### Scenario: Summary after completion
- **WHEN** a batch finishes with some skipped and some failed files
- **THEN** the page lists the skipped file names, and the failed file names each with its reason

### Requirement: Batch upload can be interrupted without corrupting state
If the batch upload stops before all files are processed (the page is closed, the connection drops, or the app is restarted), every file already reported as uploaded SHALL remain stored and visible in the collection, and no file SHALL be left partially stored.

#### Scenario: Connection lost mid-batch
- **WHEN** the connection to the app drops while a batch is running
- **THEN** the batch stops issuing further uploads, photos uploaded before the drop remain in the collection, and the interrupted file is either stored completely or not stored at all

### Requirement: Batch upload prevents concurrent submission
While a batch upload is in progress, the batch upload action SHALL be disabled and labeled to indicate the upload is running, and SHALL only be re-enabled once the batch has finished or been stopped.

#### Scenario: Starting a second batch while one is running
- **WHEN** a batch upload is in progress
- **THEN** the batch upload button is disabled and shows an in-progress label
