## MODIFIED Requirements

### Requirement: Scan skips photos that already have a sidecar unless overwrite is requested
For each supported image file in the card photos directory, `Scan` SHALL skip analysis if a sidecar already exists for it with an `AnalysisStatus` of `ok` or `uncertain`, and the request's `overwrite_existing_sidecars` flag is not set. A sidecar with an `AnalysisStatus` of `failed`, `notAnalyzed`, or any other value that isn't `ok`/`uncertain` SHALL be retried regardless of the `overwrite_existing_sidecars` flag.

#### Scenario: Existing successful sidecar, overwrite not requested
- **WHEN** an image already has a sidecar file with `AnalysisStatus` `ok` or `uncertain`, and `overwrite_existing_sidecars` is false or unset
- **THEN** the image is not sent to Gemini for analysis and is counted in the response's `skipped` count

#### Scenario: Existing failed sidecar, overwrite not requested
- **WHEN** an image already has a sidecar file with `AnalysisStatus` `failed`, and `overwrite_existing_sidecars` is false or unset
- **THEN** the image is sent to Gemini for analysis again, and its sidecar is overwritten with the new result

#### Scenario: Existing not-analyzed sidecar, overwrite not requested
- **WHEN** an image already has a sidecar file whose `AnalysisStatus` is `notAnalyzed` (for example, one created only by a manual field edit before any analysis ran), and `overwrite_existing_sidecars` is false or unset
- **THEN** the image is sent to Gemini for analysis, the same as an image with no sidecar at all

#### Scenario: Existing sidecar, overwrite requested
- **WHEN** an image already has a sidecar file and `overwrite_existing_sidecars` is true
- **THEN** the image is analyzed again and its sidecar is overwritten with the new result

#### Scenario: No existing sidecar
- **WHEN** an image has no sidecar file yet
- **THEN** the image is analyzed regardless of the `overwrite_existing_sidecars` flag
