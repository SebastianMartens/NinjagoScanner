## Purpose

Serves as the application's landing page ("/"), giving a person the entry point for triggering new photo analysis and, over time, a home for at-a-glance status information about the collection.

## ADDED Requirements

### Requirement: Overview is the application's home page
The Overview page SHALL be served at the root route ("/") of the web application.

#### Scenario: Opening the application
- **WHEN** a user navigates to "/"
- **THEN** the Overview page is displayed

### Requirement: A manual Gemini scan can be triggered
The Overview page SHALL provide a button that starts a PictureService scan of the resolved card photos directory, disables itself while the scan is running, and afterward shows a summary of processed/skipped/uncertain/failed counts (or the service's configuration-error message).

#### Scenario: Running a scan successfully
- **WHEN** a user clicks the scan button and the scan completes without a configuration error
- **THEN** the button is disabled for the duration of the scan, and a summary message with processed/skipped/uncertain/failed counts is shown afterward

#### Scenario: Scan cannot start due to configuration
- **WHEN** a user clicks the scan button and PictureService reports a configuration error
- **THEN** the shown message is PictureService's error message instead of a processed/skipped/uncertain/failed summary
