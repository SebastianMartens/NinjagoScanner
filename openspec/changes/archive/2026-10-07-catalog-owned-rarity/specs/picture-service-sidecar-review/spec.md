## MODIFIED Requirements

### Requirement: ReviewStatus changes only via explicit action
`ReviewStatus` SHALL NOT be derived, defaulted, or altered as a side effect of any other operation on a sidecar record.

#### Scenario: Rescanning a card does not change ReviewStatus
- **WHEN** a card photo with an existing `ReviewStatus` of `verified` or `incorrect` is rescanned and its `AnalysisStatus`/`Confidence`/detected fields are updated
- **THEN** its `ReviewStatus` remains unchanged

#### Scenario: Editing other sidecar fields does not change ReviewStatus
- **WHEN** any sidecar field other than `ReviewStatus` itself is updated (e.g. card name, card number, set name)
- **THEN** `ReviewStatus` is not modified by that update

#### Scenario: Confidence does not gate ReviewStatus
- **WHEN** a sidecar record has any `Confidence` value, high or low
- **THEN** `ReviewStatus` is not automatically set or changed based on that value
