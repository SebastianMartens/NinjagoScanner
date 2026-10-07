## MODIFIED Requirements

### Requirement: All sidecar-editing RPCs are scoped to a collection
`UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `UpdateReviewStatus`, and `UpdateRotation` SHALL each require a `collection_id` identifying which collection the target image belongs to, SHALL create or update the sidecar record within that collection only, and SHALL NOT create, read, or modify a sidecar record belonging to a different collection, even if the same image file name exists there.

#### Scenario: Editing a sidecar creates it within the specified collection
- **WHEN** one of the sidecar-editing RPCs is called with a `collection_id` for an image with no existing sidecar in that collection
- **THEN** the new sidecar record is created within that collection

#### Scenario: Same file name in two collections is edited independently
- **WHEN** the same image file name exists in two different collections, and a sidecar-editing RPC is called with one collection's `collection_id`
- **THEN** only that collection's sidecar record is affected; the other collection's sidecar record (if any) for the same file name is left unchanged

## ADDED Requirements

### Requirement: UpdateRotation creates a sidecar record that reports as not-analyzed if none exists
If no sidecar file exists yet for the given image, `UpdateRotation` SHALL create one before setting its rotation flag. Until that image is analyzed, its analysis status SHALL be reported as `notAnalyzed` (see `picture-service-card-listing`'s not-analyzed fallback).

#### Scenario: Rotating an unscanned image
- **WHEN** `UpdateRotation` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested rotation flag, and subsequently reading that image's card entry reports `AnalysisStatus` `notAnalyzed`

### Requirement: UpdateRotation only changes the rotation flag
If a sidecar file already exists, `UpdateRotation` SHALL update only its rotation flag, leaving every other field (analysis status, card name, card number, set name, review status, etc.) unchanged.

#### Scenario: Rotating an already-scanned card
- **WHEN** `UpdateRotation` is called for an image with an existing sidecar
- **THEN** only the sidecar's rotation flag is updated; `AnalysisStatus`, `ReviewStatus`, and all other fields keep their prior values

### Requirement: UpdateRotation does not trigger or imply re-analysis
`UpdateRotation` SHALL NOT change `AnalysisStatus` (other than the not-analyzed fallback for a brand-new record per the requirement above) and SHALL NOT trigger AI Analysis.

#### Scenario: Rotating a fully analyzed card leaves its analysis status untouched
- **WHEN** `UpdateRotation` is called for an image whose sidecar has `AnalysisStatus` `ok`
- **THEN** after the call, `AnalysisStatus` is still `ok` and no analysis run was triggered
