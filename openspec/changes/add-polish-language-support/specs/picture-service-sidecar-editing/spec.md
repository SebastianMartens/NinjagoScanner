## ADDED Requirements

### Requirement: UpdateCardLanguage creates a pending sidecar record if none exists
If no sidecar file exists yet for the given image, `UpdateCardLanguage` SHALL create one with `AnalysisStatus` `pending` before setting its `Language`.

#### Scenario: Setting the language of an unscanned image
- **WHEN** `UpdateCardLanguage` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with `AnalysisStatus` `pending` and the requested `Language`

### Requirement: UpdateCardLanguage only changes the Language field
If a sidecar file already exists, `UpdateCardLanguage` SHALL update only its `Language` field, leaving every other field (analysis status, card name, card number, set name, review status, etc.) unchanged.

#### Scenario: Correcting the language of an already-scanned card
- **WHEN** `UpdateCardLanguage` is called for an image with an existing sidecar
- **THEN** only the sidecar's `Language` is updated; `AnalysisStatus`, `CardNumber`, `SetName`, `ReviewStatus`, and all other fields keep their prior values

#### Scenario: Blank language is normalized to empty
- **WHEN** the `UpdateCardLanguage` request's language is empty or whitespace-only
- **THEN** the sidecar's `Language` field is stored as absent/null rather than as a blank string
