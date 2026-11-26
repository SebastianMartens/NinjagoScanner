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
The system SHALL reject photos that are not one of the supported image types (JPG, PNG, BMP, WEBP) or that exceed the configured maximum upload size, before the upload stream to PictureService begins. The type check SHALL happen before any downscaling is attempted. The size limit SHALL apply to the file that will actually be uploaded: the downscaled photo when downscaling succeeded, otherwise the original file.

#### Scenario: Oversized file rejected
- **WHEN** the user selects a photo larger than the configured maximum upload size, and the file that would be uploaded (after any downscaling) is still larger than that limit
- **THEN** the Web app does not start streaming the file to PictureService, and the client displays an error instead of uploading

#### Scenario: Large original accepted after downscaling
- **WHEN** the user selects a 20 MB JPEG while the configured maximum upload size is 15 MB, and downscaling produces a 500 KB photo
- **THEN** the downscaled photo is uploaded and the upload succeeds

#### Scenario: Unsupported file type rejected
- **WHEN** the user selects a file whose type is not one of the supported image types
- **THEN** the Web app does not attempt to downscale it, does not start streaming the file to PictureService, and the client displays an error instead of uploading

### Requirement: Photos are downscaled in the browser before upload
Before a JPEG or PNG photo selected in the single-photo input or the batch input is uploaded, the system SHALL downscale it in the browser so that its longest side is at most 1366 px, keeping its aspect ratio. A photo whose longest side is already 1366 px or less SHALL NOT be enlarged. The downscaled photo SHALL keep its original image format (JPEG stays JPEG, PNG stays PNG) and its original source file name. Only the downscaled bytes SHALL leave the browser. Photos of other supported types (WEBP, BMP) SHALL be uploaded unchanged.

#### Scenario: Large phone photo is downscaled
- **WHEN** a user uploads a 4000x3000 px JPEG named `IMG_0001.jpg` through the single-photo input
- **THEN** the stored photo is a JPEG 1366 px wide and 1024 or 1025 px high (aspect ratio kept, rounded to whole pixels) with source file name `IMG_0001.jpg`, and it is analyzed as usual

#### Scenario: Portrait photo is limited by its height
- **WHEN** a user uploads a 3000x4000 px JPEG
- **THEN** the stored photo's height is 1366 px and its width keeps the original aspect ratio

#### Scenario: Small photo is not enlarged
- **WHEN** a user uploads a 1000x750 px JPEG
- **THEN** the stored photo is not larger than 1000x750 px

#### Scenario: Batch photos are downscaled too
- **WHEN** a user uploads several large JPEG photos through the batch input
- **THEN** each stored photo has a longest side of at most 1366 px, is stored without analysis as before, and keeps its source file name, so skipping by file name on a later retry still works

#### Scenario: PNG stays PNG
- **WHEN** a user uploads a 3000x2000 px PNG
- **THEN** the stored photo is a PNG with a longest side of 1366 px

#### Scenario: WEBP and BMP are uploaded unchanged
- **WHEN** a user uploads a WEBP or BMP photo
- **THEN** the stored bytes are identical to the selected file

### Requirement: Downscaled photos keep their visual orientation
When a photo is downscaled, the system SHALL apply the photo's EXIF orientation to the pixels, so the stored photo shows the same way up as the original showed on the device. The stored photo SHALL NOT carry an orientation tag that would rotate it a second time. This SHALL NOT change the photo's rotation flag (see `web-photo-rotation`).

#### Scenario: Portrait phone photo with an EXIF orientation tag
- **WHEN** a user uploads a phone photo whose pixels are stored landscape with an EXIF orientation tag meaning "rotate 90°", which shows as portrait on the phone
- **THEN** the stored photo shows as portrait and upright in the gallery and on the Review page, without the user using the rotate control

#### Scenario: Rotation flag is untouched
- **WHEN** a downscaled photo is stored
- **THEN** its rotation flag is not set

### Requirement: Failed downscaling falls back to the original photo
If downscaling a photo fails, does not finish within a bounded time, or produces a file that is not smaller than the original, the system SHALL upload the original file unchanged instead. Such a fallback SHALL NOT by itself make the upload fail and SHALL NOT be shown to the user as an error. The original file SHALL still pass "File type and size validation before upload".

#### Scenario: Browser cannot decode the photo
- **WHEN** the browser cannot decode a selected JPEG for downscaling
- **THEN** the original file is uploaded as it would have been without downscaling

#### Scenario: Downscaled file would be larger
- **WHEN** the re-encoded photo is as large as the original file or larger
- **THEN** the original file is uploaded

#### Scenario: Fallback original exceeds the size limit
- **WHEN** downscaling fails and the original file is larger than the configured maximum upload size
- **THEN** the upload is rejected with the existing size error, and nothing is streamed to PictureService

#### Scenario: Fallback inside a batch
- **WHEN** downscaling fails for one file of a batch
- **THEN** that file's original is uploaded and the batch continues; the file is counted as uploaded, not as failed

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

### Requirement: Photos that have not been analyzed can be analyzed from the upload page
The batch upload section of the `/upload` page SHALL provide an action that starts a PictureService scan of the collection's photos. That scan analyzes the photos that have not been analyzed yet. The action SHALL disable itself and show an in-progress label while the scan is running. Afterward it SHALL show a summary of processed/skipped/uncertain/failed counts, or the service's configuration-error message instead. If the scan stopped early due to a transport-level failure of the analysis service, the summary SHALL also say that it stopped early and can be retried later. The action's label, in-progress label and resulting messages SHALL NOT name the AI provider or model used for analysis. The action SHALL be disabled while a batch upload is in progress.

#### Scenario: Running the analysis successfully
- **WHEN** a user clicks the analysis action and the scan completes without a configuration error and without stopping early
- **THEN** the action is disabled and shows an in-progress label for the duration of the scan, and a summary message with processed/skipped/uncertain/failed counts is shown afterward

#### Scenario: Analysis cannot start due to configuration
- **WHEN** a user clicks the analysis action and PictureService reports a configuration error
- **THEN** the shown message is PictureService's error message instead of a processed/skipped/uncertain/failed summary

#### Scenario: Analysis stops early due to repeated service failures
- **WHEN** a user clicks the analysis action and PictureService reports that the batch stopped early
- **THEN** the shown message includes the processed/skipped/uncertain/failed counts for the photos actually attempted, plus a statement that the analysis stopped early and can be retried later

#### Scenario: Labels are provider-neutral
- **WHEN** the analysis action is shown in its idle or in-progress state, or a result message is shown
- **THEN** none of these texts names the AI provider or model (for example, none contains "Gemini")

#### Scenario: Analysis is unavailable during a batch upload
- **WHEN** a batch upload is in progress
- **THEN** the analysis action is disabled

### Requirement: Batch upload never triggers analysis
Every photo uploaded through the batch input SHALL be uploaded without analysis, regardless of how many files were selected, including a selection of a single file. The single-photo input SHALL keep triggering analysis as before.

#### Scenario: Multiple files uploaded through the batch input
- **WHEN** a user uploads several files through the batch input
- **THEN** each photo is stored with analysis status `notAnalyzed` and none is analyzed as part of the upload

#### Scenario: One file uploaded through the batch input
- **WHEN** a user selects exactly one file in the batch input and uploads it
- **THEN** the photo is stored with analysis status `notAnalyzed` and is not analyzed as part of the upload

#### Scenario: Analysis is started later
- **WHEN** photos have been batch-uploaded
- **THEN** they can be analyzed afterwards through the analysis action in the same batch upload section of `/upload`, and the section's help text points to that action

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
