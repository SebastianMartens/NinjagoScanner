# picture-service-gemini-analysis Specification

## Purpose

Defines the contract of the outbound call to the Gemini API used to analyze a single card photo: what is sent, how transient failures are retried, and how the response is turned into an analysis result.

## Requirements

### Requirement: Analysis request includes the photo and a series-catalog prompt
Each analysis request SHALL send the card photo as inline image data together with a text prompt that lists the known catalog series (name, symbol/logo hint, year, and known card names where available) and instructs the model to return a fixed JSON schema.

#### Scenario: Request built for an image
- **WHEN** an image is analyzed
- **THEN** the request sent to the Gemini API contains the image bytes as inline data with the correct MIME type for the file extension, and a prompt that enumerates the series from the loaded catalog

### Requirement: Transient HTTP failures are retried with increasing delay
If the Gemini API responds with HTTP 429 or a 5xx status, the analysis call SHALL be retried up to the configured maximum number of attempts, waiting `retry_delay_ms * attempt` between attempts. Any other non-success status SHALL fail immediately without retrying.

#### Scenario: Rate limited then succeeds
- **WHEN** the Gemini API responds with 429 on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after waiting `retry_delay_ms * attempt` and the eventual successful response is used

#### Scenario: Server error exhausts all attempts
- **WHEN** the Gemini API responds with a 5xx status on every attempt up to the configured maximum
- **THEN** the analysis result is a failure describing the API error, and no further attempts are made after the limit

#### Scenario: Non-retryable error
- **WHEN** the Gemini API responds with a status other than 429 or 5xx (e.g. 400 or 404)
- **THEN** the analysis fails immediately on the first such response without retrying

### Requirement: Malformed or empty model output is treated as a failure
If the Gemini API call succeeds but the response contains no usable text, or the text is not valid JSON matching the expected payload shape, the analysis result SHALL be a failure with a descriptive error message, not a crash or an `ok`/`uncertain` result.

#### Scenario: Empty candidate text
- **WHEN** the Gemini API responds successfully but no candidate/part contains non-empty text
- **THEN** the analysis result is `failed` with a message stating no JSON result was returned

#### Scenario: Invalid JSON in model output
- **WHEN** the extracted model text cannot be parsed as the expected JSON payload
- **THEN** the analysis result is `failed` with a message describing the JSON parse failure, and the raw model text is preserved for diagnostics

### Requirement: Confidence is clamped and status is normalized from the model payload
The parsed confidence value SHALL be clamped to the range [0, 1] (treating NaN/infinite values as 0), and the analysis status SHALL be normalized to `failed` if the model reports `failed`, to `uncertain` if the model reports a non-`ok`/non-`failed` status or the confidence is below 0.65, and to `ok` otherwise.

#### Scenario: Model reports failed
- **WHEN** the model payload's status is `failed`
- **THEN** the analysis result's status is `failed` regardless of the reported confidence

#### Scenario: High confidence ok result
- **WHEN** the model payload's status is `ok` and confidence is 0.65 or higher
- **THEN** the analysis result's status is `ok`

#### Scenario: Low confidence forces uncertain
- **WHEN** the model payload's status is `ok` but confidence is below 0.65
- **THEN** the analysis result's status is `uncertain`

#### Scenario: Out-of-range confidence is clamped
- **WHEN** the model payload's confidence is negative, greater than 1, NaN, or infinite
- **THEN** the stored confidence is clamped to the nearest value within [0, 1] (0 for NaN/infinite)

### Requirement: Resolved set name is discarded for failed analyses
When the model itself reports the analysis status as `failed`, the analysis result's `SetName` SHALL be null, regardless of what series-name matching would have produced. This does not apply when the status is instead escalated to `failed` because series-name matching found no confident match while the model-reported status was `ok` or `uncertain` — in that case the model's raw set-name guess is preserved (see "A failed series-name match escalates the analysis status and preserves the raw guess").

#### Scenario: Model-reported failure clears set name
- **WHEN** the model payload's status is `failed`
- **THEN** the stored `SetName` is null even if the model payload included a set name guess

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

### Requirement: Analysis result includes a detected language
The parsed analysis result SHALL include a `Language` value normalized to one of `de`, `en`, `pl`, or `unknown`: a model-reported value matching `de`, `en`, or `pl` (case-insensitively) is kept as that value, and any other, unrecognized, or missing value is normalized to `unknown`.

#### Scenario: Model reports German
- **WHEN** the model payload's language is `de`
- **THEN** the analysis result's `Language` is `de`

#### Scenario: Model reports English in a different case
- **WHEN** the model payload's language is `EN`
- **THEN** the analysis result's `Language` is `en`

#### Scenario: Model reports Polish
- **WHEN** the model payload's language is `pl`
- **THEN** the analysis result's `Language` is `pl`

#### Scenario: Model reports a language outside the closed set
- **WHEN** the model payload's language is a value other than `de`, `en`, or `pl` (e.g. `fr`)
- **THEN** the analysis result's `Language` is `unknown`

#### Scenario: Model omits the language entirely
- **WHEN** the model payload does not include a language value
- **THEN** the analysis result's `Language` is `unknown`
