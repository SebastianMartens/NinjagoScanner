# picture-service-photo-scan Specification

## Purpose

Defines how the `Scan` RPC batch-processes a directory of card photos: validating prerequisites, deciding which photos to (re)analyze, and reporting a summary of the outcome.

## Requirements

### Requirement: Scan validates prerequisites before processing any photo
The `Scan` RPC SHALL validate its configuration and dependencies before analyzing any photo, and SHALL return a `ScanSummary` with `has_configuration_error` set and no photos processed if validation fails.

#### Scenario: Missing API key
- **WHEN** `Scan` is invoked and no Gemini API key is configured (neither in the request nor in configuration)
- **THEN** the response has `has_configuration_error` set to true and a message stating the API key is not set, and no photos are processed

#### Scenario: Card photos directory does not exist
- **WHEN** `Scan` is invoked and the resolved card photos directory does not exist on disk
- **THEN** the response has `has_configuration_error` set to true and a message naming the missing folder, and no photos are processed

#### Scenario: Catalog service unreachable
- **WHEN** `Scan` is invoked and loading the series catalog from the configured `CatalogService` address fails (connection error or exception)
- **THEN** the response has `has_configuration_error` set to true and a message naming the unreachable catalog service address, and no photos are processed

#### Scenario: Catalog service returns no series
- **WHEN** `Scan` is invoked and the catalog service responds successfully but with zero series
- **THEN** the response has `has_configuration_error` set to true and a message stating the catalog service delivered no series data, and no photos are processed

#### Scenario: No card images in directory
- **WHEN** `Scan` is invoked, all prerequisites are satisfied, and the card photos directory contains no files with a supported image extension
- **THEN** the response reports `total_images` as 0 and a message stating no card images were found, without `has_configuration_error` set

### Requirement: Scan skips photos that already have a sidecar unless overwrite is requested
For each supported image file in the card photos directory, `Scan` SHALL skip analysis if a sidecar already exists for it with an `AnalysisStatus` of `ok` or `uncertain`, and the request's `overwrite_existing_sidecars` flag is not set. A sidecar with an `AnalysisStatus` of `failed` SHALL be retried regardless of the `overwrite_existing_sidecars` flag.

#### Scenario: Existing successful sidecar, overwrite not requested
- **WHEN** an image already has a sidecar file with `AnalysisStatus` `ok` or `uncertain`, and `overwrite_existing_sidecars` is false or unset
- **THEN** the image is not sent to Gemini for analysis and is counted in the response's `skipped` count

#### Scenario: Existing failed sidecar, overwrite not requested
- **WHEN** an image already has a sidecar file with `AnalysisStatus` `failed`, and `overwrite_existing_sidecars` is false or unset
- **THEN** the image is sent to Gemini for analysis again, and its sidecar is overwritten with the new result

#### Scenario: Existing sidecar, overwrite requested
- **WHEN** an image already has a sidecar file and `overwrite_existing_sidecars` is true
- **THEN** the image is analyzed again and its sidecar is overwritten with the new result

#### Scenario: No existing sidecar
- **WHEN** an image has no sidecar file yet
- **THEN** the image is analyzed regardless of the `overwrite_existing_sidecars` flag

### Requirement: Scan processes images in a deterministic order
`Scan` SHALL enumerate supported image files in the card photos directory in ascending ordinal order by file path.

#### Scenario: Multiple images in a directory
- **WHEN** a card photos directory contains multiple supported image files
- **THEN** they are analyzed in ascending ordinal order of their file paths

### Requirement: Scan preserves an existing ReviewStatus across rescans
When re-analyzing an image that already has a sidecar with a `ReviewStatus` set, `Scan` SHALL carry that `ReviewStatus` forward onto the newly written sidecar instead of resetting it.

#### Scenario: Rescanning a reviewed card
- **WHEN** an image with `overwrite_existing_sidecars` enabled already has a sidecar whose `ReviewStatus` is `verified` or `incorrect`
- **THEN** the sidecar written after the rescan keeps that same `ReviewStatus`, even though `AnalysisStatus` and other analyzed fields are refreshed

### Requirement: Scan aborts the batch after a transport-level failure
If a single photo's analysis fails at the transport level (the Gemini API never produced a response it evaluated the photo with — see `picture-service-gemini-analysis`), `Scan` SHALL record that photo's result as `failed` with an error message, then stop processing without analyzing the remaining photos in the batch. Content-level failures (a response Gemini did evaluate, but rejected or that could not be parsed) do not abort the batch — `Scan` continues to the next photo.

#### Scenario: Transport-level failure aborts the remaining batch
- **WHEN** a photo's analysis result is a transport-level failure (Gemini API retries exhausted, an immediate non-retryable HTTP error, or an unexpected exception while attempting the analysis)
- **THEN** that photo's sidecar is written with `AnalysisStatus` `failed` and an error message, and no further photos in the batch are analyzed

#### Scenario: Content-level failure continues the batch
- **WHEN** a photo's analysis result is a content-level failure (Gemini returned a response, but it was unusable, model-rejected, or the series could not be resolved)
- **THEN** that photo's sidecar is written with `AnalysisStatus` `failed` and an error message, and subsequent photos in the batch are still processed

### Requirement: Scan reports whether it stopped early
`Scan` SHALL return a `ScanSummary` indicating whether the batch stopped early due to a transport-level failure, distinct from a configuration error (`has_configuration_error`).

#### Scenario: Batch stopped early
- **WHEN** `Scan` aborts the remaining batch after a transport-level failure
- **THEN** the returned `ScanSummary` indicates the batch stopped early, alongside accurate `processed`/`skipped`/`uncertain`/`failed` counts for the photos actually attempted

#### Scenario: Batch completes without a transport-level failure
- **WHEN** `Scan` processes every photo in the batch without hitting a transport-level failure
- **THEN** the returned `ScanSummary` does not indicate an early stop

### Requirement: Scan applies a delay between consecutive requests
`Scan` SHALL wait for the configured `delay_between_requests_ms` between processing consecutive images, and SHALL NOT wait after the last image in the batch.

#### Scenario: Delay between images
- **WHEN** `delay_between_requests_ms` is greater than zero and there is a next image to process
- **THEN** the service waits at least that many milliseconds before processing the next image

#### Scenario: No delay after the last image
- **WHEN** the current image is the last one in the batch
- **THEN** the service does not wait before returning the scan summary

### Requirement: Scan reports accurate summary counts
`Scan` SHALL return a `ScanSummary` whose `total_images`, `processed`, `skipped`, `uncertain`, and `failed` counts accurately reflect the outcome of the batch.

#### Scenario: Mixed batch outcome
- **WHEN** a batch contains images that are newly analyzed as `ok`, `uncertain`, and `failed`, plus images skipped because a sidecar already existed
- **THEN** `total_images` equals the total number of supported image files found, `processed` counts every image that was analyzed (not skipped), `skipped` counts images left untouched, and `uncertain`/`failed` count the subset of processed images with those statuses
