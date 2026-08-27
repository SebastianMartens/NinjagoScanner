# picture-service-card-listing Specification

## Purpose

Defines what the `ListCards` RPC reports for every card photo in a directory, including photos that have not been scanned yet or whose sidecar could not be read.

## Requirements

### Requirement: ListCards reports every supported image in the directory
`ListCards` SHALL return one `CardEntry` for every file with a supported image extension in the resolved card photos directory, and SHALL return an empty list if the directory does not exist.

#### Scenario: Directory with images
- **WHEN** `ListCards` is called against a directory containing supported image files
- **THEN** the response contains exactly one `CardEntry` per supported image file

#### Scenario: Directory does not exist
- **WHEN** `ListCards` is called and the resolved card photos directory does not exist
- **THEN** the response contains an empty list of cards

### Requirement: Image with no sidecar reports a pending entry
For an image file that has no sidecar file yet, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `pending`, `ReviewStatus` `unreviewed`, `Language` defaulted to German (`de`), and no other card data fields populated.

#### Scenario: Unscanned image
- **WHEN** an image file has never been scanned and has no sidecar file
- **THEN** its `CardEntry` has `AnalysisStatus` `pending`, `ReviewStatus` `unreviewed`, `Language` `de`, and empty card name/number/set name/rarity fields

### Requirement: Image with an unreadable sidecar reports a failed entry
For an image file whose sidecar file exists but cannot be read or parsed, `ListCards` SHALL return a `CardEntry` with `AnalysisStatus` `failed`, `ReviewStatus` `unreviewed`, `Language` defaulted to German (`de`), and an error message describing the read failure, rather than raising an error for the whole call.

#### Scenario: Corrupt sidecar file
- **WHEN** an image's sidecar file exists but is not valid JSON or cannot otherwise be read
- **THEN** its `CardEntry` has `AnalysisStatus` `failed`, `ReviewStatus` `unreviewed`, `Language` `de`, and a non-empty error message, and the call still returns entries for the remaining images

### Requirement: Image with a readable sidecar reports its stored data
For an image file whose sidecar file exists and can be read, `ListCards` SHALL return a `CardEntry` populated from the sidecar's stored fields (analysis status, card name, card number, set name, rarity, confidence, reasoning summary, detected text, scanned-at timestamp, error message, review status, and language — defaulted to German (`de`) if the sidecar has no explicit `Language` value).

#### Scenario: Successfully scanned image
- **WHEN** an image has a valid sidecar file from a prior scan
- **THEN** its `CardEntry` reflects that sidecar's stored `AnalysisStatus`, card data, confidence, `ReviewStatus`, and `Language`

### Requirement: Language defaults to German when not explicitly recorded
For an image whose sidecar can be read but was written before this field existed (no `Language` property present in the stored JSON), `ListCards` SHALL report `Language` as `de` without requiring the sidecar file to be rewritten or the image to be re-analyzed. An explicit stored `Language` of `unknown` (from a completed analysis that could not determine the language) SHALL be reported as `unknown`, not defaulted to `de`.

#### Scenario: Legacy sidecar predating the Language field
- **WHEN** a sidecar file is read that has no `Language` property because it was written before this feature existed
- **THEN** its `CardEntry` reports `Language` as `de`, and the sidecar file on disk is left unmodified

#### Scenario: Explicit unknown language is preserved
- **WHEN** a sidecar's stored `Language` is `unknown`
- **THEN** its `CardEntry` reports `Language` as `unknown`, not `de`

### Requirement: ListCards includes a ready-to-use download URL on every entry
`ListCards` SHALL include a working, short-lived download URL on every `CardEntry` it returns, so callers can display or link to the photo without a separate request for its download URL.

#### Scenario: Every entry carries a download URL
- **WHEN** `ListCards` is called against a directory containing photos
- **THEN** every returned `CardEntry` includes a download URL that can be used immediately to fetch that photo's bytes

### Requirement: ListCards resolves photo existence and sidecar data via bulk reads
`ListCards` SHALL determine which photos exist and read their sidecar data using a bounded, small number of bulk operations, rather than issuing one existence check or one sidecar read per photo, so its response time does not grow linearly with the number of photos.

#### Scenario: Listing hundreds of photos
- **WHEN** `ListCards` is called against a directory containing hundreds of photos
- **THEN** determining which photos exist and reading their sidecar data together take a bounded, small number of underlying storage requests rather than one pair of requests per photo
