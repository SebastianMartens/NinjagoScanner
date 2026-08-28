## MODIFIED Requirements

### Requirement: A manual Gemini scan can be triggered
The Overview page SHALL provide a button that starts a PictureService scan of the resolved card photos directory, disables itself while the scan is running, and afterward shows a summary of processed/skipped/uncertain/failed counts (or the service's configuration-error message). If the scan stopped early due to a transport-level Gemini failure, the summary SHALL also indicate that it stopped early, so the person knows the remaining photos still need a later scan.

#### Scenario: Running a scan successfully
- **WHEN** a user clicks the scan button and the scan completes without a configuration error and without stopping early
- **THEN** the button is disabled for the duration of the scan, and a summary message with processed/skipped/uncertain/failed counts is shown afterward

#### Scenario: Scan cannot start due to configuration
- **WHEN** a user clicks the scan button and PictureService reports a configuration error
- **THEN** the shown message is PictureService's error message instead of a processed/skipped/uncertain/failed summary

#### Scenario: Scan stops early due to repeated Gemini failures
- **WHEN** a user clicks the scan button and PictureService reports that the batch stopped early
- **THEN** the shown message includes the processed/skipped/uncertain/failed counts for the photos actually attempted, plus an indication that the scan stopped early and can be retried later
