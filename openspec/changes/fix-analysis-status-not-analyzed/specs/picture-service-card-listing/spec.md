## MODIFIED Requirements

### Requirement: Image with no sidecar reports a not-analyzed entry
For an image file that has no sidecar file yet, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `notAnalyzed`, `ReviewStatus` `unreviewed`, `Language` defaulted to German (`de`), and no other card data fields populated.

#### Scenario: Unscanned image
- **WHEN** an image file has never been scanned and has no sidecar file
- **THEN** its `CardEntry` has `AnalysisStatus` `notAnalyzed`, `ReviewStatus` `unreviewed`, `Language` `de`, and empty card name/number/set name/rarity fields

### Requirement: Image with a readable sidecar reports its stored data
For an image file whose sidecar file exists and can be read, `ListCards` SHALL return a `CardEntry` populated from the sidecar's stored fields (card name, card number, set name, rarity, confidence, reasoning summary, detected text, scanned-at timestamp, error message, review status, and language — defaulted to German (`de`) if the sidecar has no explicit `Language` value), where `AnalysisStatus` is reported as the stored value if it case-insensitively matches `ok`, `uncertain`, or `failed`, and as `notAnalyzed` otherwise (see "Unrecognized or missing stored analysis status reports as not-analyzed").

#### Scenario: Successfully scanned image
- **WHEN** an image has a valid sidecar file from a prior scan with a recognized `AnalysisStatus`
- **THEN** its `CardEntry` reflects that sidecar's stored `AnalysisStatus`, card data, confidence, `ReviewStatus`, and `Language`

## ADDED Requirements

### Requirement: Unrecognized or missing stored analysis status reports as not-analyzed
`ListCards` SHALL report `AnalysisStatus` as `notAnalyzed` for a readable sidecar whose stored `AnalysisStatus` is missing, blank, or does not case-insensitively match `ok`, `uncertain`, or `failed` — including legacy values recorded before this status was renamed (e.g. `pending`) — without requiring any stored data to be migrated or rewritten.

#### Scenario: Legacy pending value
- **WHEN** a sidecar's stored `AnalysisStatus` is the legacy value `pending`
- **THEN** its `CardEntry` reports `AnalysisStatus` `notAnalyzed`, and the sidecar's stored value is left unchanged

#### Scenario: Sidecar created without an explicit analysis status
- **WHEN** a sidecar record exists but has no `AnalysisStatus` value stored (for example, one created by a manual field edit before any analysis ran)
- **THEN** its `CardEntry` reports `AnalysisStatus` `notAnalyzed`
