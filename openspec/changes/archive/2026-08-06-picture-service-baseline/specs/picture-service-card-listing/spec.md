## Purpose

Defines what the `ListCards` RPC reports for every card photo in a directory, including photos that have not been scanned yet or whose sidecar could not be read.

## ADDED Requirements

### Requirement: ListCards reports every supported image in the directory
`ListCards` SHALL return one `CardEntry` for every file with a supported image extension in the resolved card photos directory, and SHALL return an empty list if the directory does not exist.

#### Scenario: Directory with images
- **WHEN** `ListCards` is called against a directory containing supported image files
- **THEN** the response contains exactly one `CardEntry` per supported image file

#### Scenario: Directory does not exist
- **WHEN** `ListCards` is called and the resolved card photos directory does not exist
- **THEN** the response contains an empty list of cards

### Requirement: Image with no sidecar reports a pending entry
For an image file that has no sidecar file yet, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `pending` and `ReviewStatus` `unreviewed`, with no card data fields populated.

#### Scenario: Unscanned image
- **WHEN** an image file has never been scanned and has no sidecar file
- **THEN** its `CardEntry` has `AnalysisStatus` `pending`, `ReviewStatus` `unreviewed`, and empty card name/number/set name/rarity fields

### Requirement: Image with an unreadable sidecar reports a failed entry
For an image file whose sidecar file exists but cannot be read or parsed, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `failed`, `ReviewStatus` `unreviewed`, and an error message describing the read failure, rather than raising an error for the whole call.

#### Scenario: Corrupt sidecar file
- **WHEN** an image's sidecar file exists but is not valid JSON or cannot otherwise be read
- **THEN** its `CardEntry` has `AnalysisStatus` `failed`, `ReviewStatus` `unreviewed`, and a non-empty error message, and the call still returns entries for the remaining images

### Requirement: Image with a readable sidecar reports its stored data
For an image file whose sidecar file exists and can be read, `ListCards` SHALL return a `CardEntry` populated from the sidecar's stored fields (analysis status, card name, card number, set name, rarity, confidence, reasoning summary, detected text, scanned-at timestamp, error message, and review status).

#### Scenario: Successfully scanned image
- **WHEN** an image has a valid sidecar file from a prior scan
- **THEN** its `CardEntry` reflects that sidecar's stored `AnalysisStatus`, card data, confidence, and `ReviewStatus`
