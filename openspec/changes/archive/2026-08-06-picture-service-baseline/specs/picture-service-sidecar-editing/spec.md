## Purpose

Defines the two RPCs that let a human directly edit a card's sidecar record outside of the scan pipeline: a full field update and a set-name-only update.

## ADDED Requirements

### Requirement: UpdateSidecar creates a sidecar record if none exists
If no sidecar file exists yet for the given image, `UpdateSidecar` SHALL create one seeded with the image's source file name and path before applying the requested field values.

#### Scenario: Editing an unscanned image
- **WHEN** `UpdateSidecar` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested field values and the image's source file name and path recorded

### Requirement: UpdateSidecar overwrites all editable fields on the existing record
If a sidecar file already exists, `UpdateSidecar` SHALL merge the requested values onto it, overwriting `AnalysisStatus`, card name, card number, set name, rarity, confidence, reasoning summary, detected text, error message, and `ReviewStatus`, while preserving the record's existing source file identity fields.

#### Scenario: Editing an existing sidecar
- **WHEN** `UpdateSidecar` is called for an image with an existing sidecar
- **THEN** the sidecar's editable fields are replaced with the request's values, and its source file name/path/sidecar path are left unchanged

#### Scenario: Blank string fields are normalized to empty
- **WHEN** a text field in the `UpdateSidecar` request is empty or whitespace-only
- **THEN** the corresponding sidecar field is stored as absent/null rather than as a blank string

#### Scenario: Detected text entries are trimmed and blanks removed
- **WHEN** the `UpdateSidecar` request's detected text list contains blank or whitespace-only entries
- **THEN** those entries are dropped and the remaining entries are trimmed before being stored

### Requirement: UpdateSetName creates a pending sidecar record if none exists
If no sidecar file exists yet for the given image, `UpdateSetName` SHALL create one with `AnalysisStatus` `pending` before setting its set name.

#### Scenario: Setting the name of an unscanned image
- **WHEN** `UpdateSetName` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with `AnalysisStatus` `pending` and the requested set name

### Requirement: UpdateSetName only changes the set name field
If a sidecar file already exists, `UpdateSetName` SHALL update only its `SetName` field, leaving every other field (analysis status, card name, confidence, review status, etc.) unchanged.

#### Scenario: Renaming the series of an already-scanned card
- **WHEN** `UpdateSetName` is called for an image with an existing sidecar
- **THEN** only the sidecar's `SetName` is updated; `AnalysisStatus`, `ReviewStatus`, and all other fields keep their prior values
