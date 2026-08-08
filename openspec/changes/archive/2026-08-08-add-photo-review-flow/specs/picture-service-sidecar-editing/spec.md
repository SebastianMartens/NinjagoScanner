## ADDED Requirements

### Requirement: UpdateReviewStatus creates a pending sidecar record if none exists
If no sidecar file exists yet for the given image, `UpdateReviewStatus` SHALL create one with `AnalysisStatus` `pending` before setting its `ReviewStatus`.

#### Scenario: Setting the review status of an unscanned image
- **WHEN** `UpdateReviewStatus` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with `AnalysisStatus` `pending` and the requested `ReviewStatus`

### Requirement: UpdateReviewStatus only changes the ReviewStatus field
If a sidecar file already exists, `UpdateReviewStatus` SHALL update only its `ReviewStatus` field, leaving every other field (analysis status, card name, card number, set name, confidence, etc.) unchanged.

#### Scenario: Confirming an already-scanned card
- **WHEN** `UpdateReviewStatus` is called for an image with an existing sidecar
- **THEN** only the sidecar's `ReviewStatus` is updated; `AnalysisStatus`, `SetName`, and all other fields keep their prior values
