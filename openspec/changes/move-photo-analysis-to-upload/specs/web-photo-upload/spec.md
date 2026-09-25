## ADDED Requirements

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

## MODIFIED Requirements

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
