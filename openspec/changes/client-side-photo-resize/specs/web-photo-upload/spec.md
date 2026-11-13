## ADDED Requirements

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

## MODIFIED Requirements

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
