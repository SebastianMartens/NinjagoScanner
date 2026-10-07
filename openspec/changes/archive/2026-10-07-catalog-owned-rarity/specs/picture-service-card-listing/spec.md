## MODIFIED Requirements

### Requirement: Image with no sidecar reports a not-analyzed entry
For an image file that has no sidecar file yet, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `notAnalyzed`, `ReviewStatus` `unreviewed`, `Language` defaulted to German (`de`), and no other card data fields populated.

#### Scenario: Unscanned image
- **WHEN** an image file has never been scanned and has no sidecar file
- **THEN** its `CardEntry` has `AnalysisStatus` `notAnalyzed`, `ReviewStatus` `unreviewed`, `Language` `de`, and empty card name/number/set name fields

### Requirement: Image with a readable sidecar reports its stored data
For an image file whose sidecar file exists and can be read, `ListCards` SHALL return a `CardEntry` populated from the sidecar's stored fields (card name, card number, set name, confidence, reasoning summary, detected text, scanned-at timestamp, error message, review status, and language - defaulted to German (`de`) if the sidecar has no explicit `Language` value), where `AnalysisStatus` is reported as the stored value if it case-insensitively matches `ok`, `uncertain`, or `failed`, and as `notAnalyzed` otherwise (see "Unrecognized or missing stored analysis status reports as not-analyzed"). A `CardEntry` SHALL NOT carry a rarity.

#### Scenario: Successfully scanned image
- **WHEN** an image has a valid sidecar file from a prior scan
- **THEN** its `CardEntry` reflects that sidecar's stored `AnalysisStatus`, card data, confidence, `ReviewStatus`, and `Language`
