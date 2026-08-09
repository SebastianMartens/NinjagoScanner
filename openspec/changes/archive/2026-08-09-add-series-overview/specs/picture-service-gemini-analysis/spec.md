## MODIFIED Requirements

### Requirement: Resolved set name is discarded for failed analyses
When the model itself reports the analysis status as `failed`, the analysis result's `SetName` SHALL be null, regardless of what series-name matching would have produced. This does not apply when the status is instead escalated to `failed` because series-name matching found no confident match while the model-reported status was `ok` or `uncertain` — in that case the model's raw set-name guess is preserved (see "A failed series-name match escalates the analysis status and preserves the raw guess").

#### Scenario: Model-reported failure clears set name
- **WHEN** the model payload's status is `failed`
- **THEN** the stored `SetName` is null even if the model payload included a set name guess

## ADDED Requirements

### Requirement: A failed series-name match escalates the analysis status and preserves the raw guess
When the model-reported status is `ok` or `uncertain` but series-name matching finds no confident match (no exact or unambiguous evidence-based match), the analysis result's status SHALL be escalated to `failed`, and the stored `SetName` SHALL be the model's own raw, trimmed set-name guess rather than null.

#### Scenario: Confident card read, unresolved series
- **WHEN** the model reports status `ok` with confidence 0.65 or higher, but series-name matching finds no confident catalog series match
- **THEN** the analysis result's status is `failed`, and the stored `SetName` is the model's original set-name guess, unchanged from what it reported

#### Scenario: Uncertain card read, unresolved series
- **WHEN** the model reports status `uncertain` (or reports `ok` with confidence below 0.65), and series-name matching finds no confident catalog series match
- **THEN** the analysis result's status is `failed` (escalated from `uncertain`), and the stored `SetName` is the model's original set-name guess

#### Scenario: A confident series match is not escalated
- **WHEN** series-name matching resolves an exact or unambiguous evidence-based series match
- **THEN** the analysis result's status is not escalated because of series matching, and the stored `SetName` is the resolved canonical catalog series name
