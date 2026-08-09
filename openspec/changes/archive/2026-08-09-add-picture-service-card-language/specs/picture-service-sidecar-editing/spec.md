## MODIFIED Requirements

### Requirement: UpdateSidecar overwrites all editable fields on the existing record
If a sidecar file already exists, `UpdateSidecar` SHALL merge the requested values onto it, overwriting `AnalysisStatus`, card name, card number, set name, rarity, language, confidence, reasoning summary, detected text, error message, and `ReviewStatus`, while preserving the record's existing source file identity fields.

#### Scenario: Editing an existing sidecar
- **WHEN** `UpdateSidecar` is called for an image with an existing sidecar
- **THEN** the sidecar's editable fields are replaced with the request's values, and its source file name/path/sidecar path are left unchanged

#### Scenario: Blank string fields are normalized to empty
- **WHEN** a text field in the `UpdateSidecar` request is empty or whitespace-only
- **THEN** the corresponding sidecar field is stored as absent/null rather than as a blank string

#### Scenario: Detected text entries are trimmed and blanks removed
- **WHEN** the `UpdateSidecar` request's detected text list contains blank or whitespace-only entries
- **THEN** those entries are dropped and the remaining entries are trimmed before being stored
