## MODIFIED Requirements

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

### Requirement: Scan aborts the batch after a transport-level failure
If a single photo's analysis fails at the transport level (the Gemini API never produced a response it evaluated the photo with — see `picture-service-gemini-analysis`), `Scan` SHALL record that photo's result as `failed` with an error message, then stop processing without analyzing the remaining photos in the batch. Content-level failures (a response Gemini did evaluate, but rejected or that could not be parsed) do not abort the batch — `Scan` continues to the next photo.

#### Scenario: Transport-level failure aborts the remaining batch
- **WHEN** a photo's analysis result is a transport-level failure (Gemini API retries exhausted, an immediate non-retryable HTTP error, or an unexpected exception while attempting the analysis)
- **THEN** that photo's sidecar is written with `AnalysisStatus` `failed` and an error message, and no further photos in the batch are analyzed

#### Scenario: Content-level failure continues the batch
- **WHEN** a photo's analysis result is a content-level failure (Gemini returned a response, but it was unusable, model-rejected, or the series could not be resolved)
- **THEN** that photo's sidecar is written with `AnalysisStatus` `failed` and an error message, and subsequent photos in the batch are still processed

## ADDED Requirements

### Requirement: Scan reports whether it stopped early
`Scan` SHALL return a `ScanSummary` indicating whether the batch stopped early due to a transport-level failure, distinct from a configuration error (`has_configuration_error`).

#### Scenario: Batch stopped early
- **WHEN** `Scan` aborts the remaining batch after a transport-level failure
- **THEN** the returned `ScanSummary` indicates the batch stopped early, alongside accurate `processed`/`skipped`/`uncertain`/`failed` counts for the photos actually attempted

#### Scenario: Batch completes without a transport-level failure
- **WHEN** `Scan` processes every photo in the batch without hitting a transport-level failure
- **THEN** the returned `ScanSummary` does not indicate an early stop
