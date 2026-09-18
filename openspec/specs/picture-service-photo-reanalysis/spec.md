# picture-service-photo-reanalysis Specification

## Purpose

Defines the `ReanalyzePhoto` RPC, which re-runs AI Analysis on one already-stored card photo on demand and replaces that photo's previous analysis result, so a wrong or failed analysis can be redone without deleting and re-uploading the photo.

## Requirements

### Requirement: ReanalyzePhoto requires a collection and an existing photo
`ReanalyzePhoto` SHALL require a `photo_id` and a `collection_id`, and SHALL reject the call with `InvalidArgument` if either is missing or blank. It SHALL fail with `NotFound`, and SHALL NOT modify any sidecar record, when no photo with that `photo_id` exists within that `collection_id` - including when the `photo_id` exists only in a different collection.

#### Scenario: Missing photo_id
- **WHEN** `ReanalyzePhoto` is called with a blank `photo_id`
- **THEN** the call fails with `InvalidArgument`

#### Scenario: Missing collection_id
- **WHEN** `ReanalyzePhoto` is called with a blank `collection_id`
- **THEN** the call fails with `InvalidArgument`

#### Scenario: Photo does not exist
- **WHEN** `ReanalyzePhoto` is called with a `photo_id` that has no stored photo in the given collection
- **THEN** the call fails with `NotFound` and no sidecar record is created or changed

#### Scenario: Photo belongs to a different collection
- **WHEN** `ReanalyzePhoto` is called with a `photo_id` that exists only under a different `collection_id`
- **THEN** the call fails with `NotFound` and neither collection's data is changed

### Requirement: ReanalyzePhoto runs the full analysis regardless of the existing analysis status
`ReanalyzePhoto` SHALL run the same AI Analysis that `UploadPhoto` runs after storing a new photo, against the photo's stored bytes, and SHALL write the outcome to that photo's sidecar. It SHALL do so whether the photo's current `AnalysisStatus` is `ok`, `uncertain`, `failed`, or `notAnalyzed`, and whether or not it already has a sidecar record - it is never skipped the way `Scan` skips photos that already have a completed analysis. The new outcome replaces the previous analysis result, including the detected and derived attribute data and the scanned-at timestamp.

#### Scenario: Re-analyzing a photo that was analyzed successfully
- **WHEN** `ReanalyzePhoto` is called for a photo whose sidecar has `AnalysisStatus` `ok`
- **THEN** the analysis runs again and the sidecar's analysis result, detected and derived attributes, and scanned-at timestamp reflect the new run

#### Scenario: Re-analyzing a photo whose analysis failed
- **WHEN** `ReanalyzePhoto` is called for a photo whose sidecar has `AnalysisStatus` `failed`
- **THEN** the analysis runs again and, if it succeeds, the sidecar's `AnalysisStatus` is no longer `failed` and its error message is cleared

#### Scenario: Re-analyzing a photo that has no sidecar yet
- **WHEN** `ReanalyzePhoto` is called for a stored photo that has no sidecar record
- **THEN** the analysis runs and a sidecar record is created holding its result

### Requirement: ReanalyzePhoto preserves review status and pins a verified match
`ReanalyzePhoto` SHALL leave the photo's `ReviewStatus` exactly as it is at the moment the result is written, never resetting or setting it. When the photo's `ReviewStatus` is `verified` and its sidecar has both a series name and a card number, the analysis SHALL still re-run, but the resulting sidecar SHALL keep that series name and card number instead of a newly matched pair, consistent with how `Scan` treats verified photos. Every other analysis-derived field (card name, rarity, language, and the series name and card number of a photo that is not verified) SHALL be replaced by the new result, including values a person had previously edited by hand.

#### Scenario: Review status survives re-analysis
- **WHEN** `ReanalyzePhoto` completes for a photo whose `ReviewStatus` is `incorrect`
- **THEN** the photo's `ReviewStatus` is still `incorrect`

#### Scenario: Verified series and card number survive re-analysis
- **WHEN** `ReanalyzePhoto` completes for a `verified` photo with series name "Serie 3" and card number "12", and the new analysis would otherwise have matched a different series and card number
- **THEN** the sidecar's series name is still "Serie 3" and its card number is still "12"

#### Scenario: A photo that is not verified takes the new match
- **WHEN** `ReanalyzePhoto` completes for an `unreviewed` photo and the new analysis matches a different series and card number than before
- **THEN** the sidecar holds the newly matched series name and card number

#### Scenario: A review status change made during the analysis is not lost
- **WHEN** a photo's `ReviewStatus` is changed while its `ReanalyzePhoto` call is still running
- **THEN** the sidecar written when the analysis finishes carries the changed `ReviewStatus`, not the value it had when the call began

### Requirement: ReanalyzePhoto failure handling protects the existing analysis
When `ReanalyzePhoto` cannot run the analysis because its prerequisites are unavailable - no Gemini API key configured (`FailedPrecondition`) or CatalogService unreachable (`Unavailable`) - it SHALL fail with that error and leave the photo's sidecar unchanged. When the analysis starts but Gemini cannot be reached at the transport level, it SHALL fail with `Unavailable` and leave the sidecar unchanged, so a transient outage never replaces a previous result with a `failed` one. When Gemini is reached but its response cannot be used (a content-level failure), `ReanalyzePhoto` SHALL record the outcome in the sidecar with `AnalysisStatus` `failed` and an error message, as `UploadPhoto` and `Scan` do, and SHALL return the updated card normally.

#### Scenario: Gemini is unreachable
- **WHEN** `ReanalyzePhoto` is called and Gemini cannot be reached at the transport level
- **THEN** the call fails with `Unavailable` and the photo's sidecar is unchanged, including its previous `AnalysisStatus`

#### Scenario: No API key is configured
- **WHEN** `ReanalyzePhoto` is called and no Gemini API key is configured
- **THEN** the call fails with `FailedPrecondition` and the photo's sidecar is unchanged

#### Scenario: CatalogService is unreachable
- **WHEN** `ReanalyzePhoto` is called and CatalogService cannot be reached
- **THEN** the call fails with `Unavailable` and the photo's sidecar is unchanged

#### Scenario: Gemini answers with an unusable response
- **WHEN** `ReanalyzePhoto` is called and Gemini is reached but returns a response the analysis cannot use
- **THEN** the sidecar's `AnalysisStatus` becomes `failed` with an error message, and the call returns the updated card rather than an error

### Requirement: ReanalyzePhoto returns the updated card and is immediately visible
On success, `ReanalyzePhoto` SHALL return the photo's card entry as it stands after the sidecar was written. Any subsequent read of that photo's sidecar data - `ListCards`, `GetCardDetails` - SHALL reflect the new analysis result without waiting for any cache to expire.

#### Scenario: Response reflects the new result
- **WHEN** `ReanalyzePhoto` succeeds
- **THEN** the returned card entry carries the photo's newly written analysis status, card name, card number, series name, rarity, and language

#### Scenario: Subsequent reads see the new result
- **WHEN** `ListCards` or `GetCardDetails` is called for the photo after `ReanalyzePhoto` succeeded
- **THEN** the data returned reflects the new analysis result
