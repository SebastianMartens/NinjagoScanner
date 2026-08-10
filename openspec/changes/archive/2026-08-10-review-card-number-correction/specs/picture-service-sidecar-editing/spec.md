## ADDED Requirements

### Requirement: UpdateCardNumber creates a pending sidecar record if none exists
If no sidecar file exists yet for the given image, `UpdateCardNumber` SHALL create one with `AnalysisStatus` `pending` before setting its `CardNumber`.

#### Scenario: Correcting the card number of an unscanned image
- **WHEN** `UpdateCardNumber` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with `AnalysisStatus` `pending` and the requested `CardNumber`

### Requirement: UpdateCardNumber only changes the CardNumber field
If a sidecar file already exists, `UpdateCardNumber` SHALL update only its `CardNumber` field, leaving every other field (analysis status, card name, set name, review status, etc.) unchanged.

#### Scenario: Correcting the card number of an already-scanned card
- **WHEN** `UpdateCardNumber` is called for an image with an existing sidecar
- **THEN** only the sidecar's `CardNumber` is updated; `AnalysisStatus`, `SetName`, `ReviewStatus`, and all other fields keep their prior values

#### Scenario: Blank card number is normalized to empty
- **WHEN** the `UpdateCardNumber` request's card number is empty or whitespace-only
- **THEN** the sidecar's `CardNumber` field is stored as absent/null rather than as a blank string
