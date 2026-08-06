# picture-service-sidecar-review Specification

## Purpose

Gives every scanned card photo two independent status signals in its sidecar data: an `AnalysisStatus` produced automatically by the Gemini analysis pipeline, and a `ReviewStatus` that only a human can set, so machine output and human validation are never conflated.

## Requirements

### Requirement: Sidecar records an AnalysisStatus
Every sidecar record SHALL carry an `AnalysisStatus` field populated by the Gemini analysis pipeline, with the same values and derivation rules previously provided under the field named `Status`: `ok`, `uncertain`, `failed`, or `pending` (no sidecar yet), including the existing confidence-based downgrade to `uncertain`.

#### Scenario: Successful high-confidence scan
- **WHEN** a card photo is analyzed and Gemini returns a parseable result with confidence at or above the uncertainty threshold
- **THEN** the sidecar's `AnalysisStatus` is `ok`

#### Scenario: Low-confidence scan
- **WHEN** a card photo is analyzed and the returned confidence is below the uncertainty threshold, or Gemini itself reports an uncertain status
- **THEN** the sidecar's `AnalysisStatus` is `uncertain`

#### Scenario: Analysis failure
- **WHEN** the Gemini API call fails or its response cannot be parsed
- **THEN** the sidecar's `AnalysisStatus` is `failed`

#### Scenario: No sidecar yet
- **WHEN** a card photo has never been scanned and no sidecar file exists
- **THEN** its `AnalysisStatus` is reported as `pending`

### Requirement: Sidecar records an independent ReviewStatus
Every sidecar record SHALL carry a `ReviewStatus` field, separate from `AnalysisStatus` and separate from `Confidence`, representing whether a human has manually validated the detected card data against the photo. Allowed values are `unreviewed`, `verified`, and `incorrect`.

#### Scenario: Default for new and pre-existing cards
- **WHEN** a sidecar record has never had a `ReviewStatus` explicitly set (a new scan, or an existing card from before this field existed)
- **THEN** its `ReviewStatus` is `unreviewed`

#### Scenario: Human confirms detected data is correct
- **WHEN** a human explicitly sets `ReviewStatus` to `verified` for a card
- **THEN** the sidecar record persists `ReviewStatus` as `verified` until explicitly changed again

#### Scenario: Human flags detected data as wrong or incomplete
- **WHEN** a human explicitly sets `ReviewStatus` to `incorrect` for a card because the detected card number, series, or other data does not match the photo or is missing
- **THEN** the sidecar record persists `ReviewStatus` as `incorrect` until explicitly changed again

### Requirement: ReviewStatus changes only via explicit action
`ReviewStatus` SHALL NOT be derived, defaulted, or altered as a side effect of any other operation on a sidecar record.

#### Scenario: Rescanning a card does not change ReviewStatus
- **WHEN** a card photo with an existing `ReviewStatus` of `verified` or `incorrect` is rescanned and its `AnalysisStatus`/`Confidence`/detected fields are updated
- **THEN** its `ReviewStatus` remains unchanged

#### Scenario: Editing other sidecar fields does not change ReviewStatus
- **WHEN** any sidecar field other than `ReviewStatus` itself is updated (e.g. card name, card number, set name, rarity)
- **THEN** `ReviewStatus` is not modified by that update

#### Scenario: Confidence does not gate ReviewStatus
- **WHEN** a sidecar record has any `Confidence` value, high or low
- **THEN** `ReviewStatus` is not automatically set or changed based on that value

### Requirement: Legacy sidecar files remain usable and can be migrated
A sidecar record whose JSON still uses the legacy `status` key (written before `AnalysisStatus` existed) SHALL have its value surfaced as `AnalysisStatus` when read, without requiring the card to be rescanned. A dedicated migration operation SHALL be available to rewrite such files to store the value under the `AnalysisStatus` key instead, and SHALL be safe to run repeatedly (files already using `AnalysisStatus` are left unchanged).

#### Scenario: Reading a legacy sidecar file
- **WHEN** a sidecar file on disk has a `status` key but no `AnalysisStatus` key
- **THEN** the record's `AnalysisStatus` reflects the legacy `status` value when read

#### Scenario: Migrating legacy sidecar files
- **WHEN** the migration operation is run against a directory containing sidecar files with the legacy `status` key
- **THEN** each such file is rewritten to store its status under the `AnalysisStatus` key with the same value it had before

#### Scenario: Migration is idempotent
- **WHEN** the migration operation is run again against a directory that was already migrated
- **THEN** files already using `AnalysisStatus` are left unchanged and no error occurs
