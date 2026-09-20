# picture-service-derived-attributes Specification

## Purpose

Defines the second stage of card analysis: a text-only Gemini call that infers a coarse card class and other derived attributes from the first stage's detected attributes, without re-examining the photo.

## Requirements

### Requirement: Derived-attribute request is text-only and does not include the photo
Each derived-attribute request SHALL be built from the attribute-detection stage's key-value output as text, and SHALL NOT include the photo's image data.

#### Scenario: Request built from detected attributes
- **WHEN** derived attributes are computed for a photo that was successfully analyzed by attribute detection
- **THEN** the request sent to the Gemini API contains the detected attributes as text content and no image data

#### Scenario: Derivation does not run without detected attributes
- **WHEN** attribute detection failed for a photo (transport-level or content-level)
- **THEN** the derived-attributes stage does not run for that photo

### Requirement: Derived attributes are a generic, flat key-value map
The parsed result of a successful derived-attributes call SHALL be a map of string keys to scalar values (string, number, or boolean), on the same terms as attribute detection's output (see `picture-service-attribute-detection`).

#### Scenario: Successful derivation produces a key-value map
- **WHEN** the Gemini API returns a successful, parseable response to a derived-attributes request
- **THEN** the derived-attributes result is a flat map of keys to scalar values

### Requirement: Derived class is one of the catalog's fixed class values
When the derived attributes include a `class` value, it SHALL be one of the catalog's fixed set of card classes (`character`, `action`, `vehicle`, `puzzle-piece`, `trap`, `limited edition`, `art`). A value outside this set SHALL be treated as if no class was derived, not passed through as-is.

#### Scenario: Recognized class value
- **WHEN** the derived attributes include a `class` value matching one of the fixed set
- **THEN** that value is kept as the derived class

#### Scenario: Unrecognized class value
- **WHEN** the derived attributes include a `class` value that does not match any value in the fixed set
- **THEN** the derived class is treated as absent rather than storing the unrecognized value

### Requirement: Transient failures are retried with increasing delay
If the Gemini API call underlying derived-attribute computation fails with a transient condition (rate limiting, a server error, or a transient connection or timeout error), the call SHALL be retried under the retry policy defined in `picture-service-gemini-analysis`: up to the configured maximum number of attempts in total, waiting an exponentially growing, jittered delay between attempts. A non-transient failure SHALL fail immediately without retrying.

#### Scenario: Rate limited then succeeds
- **WHEN** the underlying call is rate-limited on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after an exponentially growing, jittered wait and the eventual successful response is used

#### Scenario: Retries exhausted
- **WHEN** the underlying call fails with a transient condition on every attempt up to the configured maximum
- **THEN** derived-attribute computation fails with a transport-level failure and no further attempts are made

#### Scenario: Non-retryable failure
- **WHEN** the underlying call fails with a condition that is not transient
- **THEN** derived-attribute computation fails immediately with a transport-level failure, without retrying

### Requirement: A failure result indicates whether Gemini evaluated the input
Every failed derived-attributes result SHALL indicate whether the failure is transport-level (Gemini never produced a response) or content-level (Gemini returned a response, but it was unusable or malformed).

#### Scenario: Malformed or empty model output
- **WHEN** the call succeeds but the response contains no usable text, or the text cannot be parsed into a flat key-value map
- **THEN** the failure result is marked as a content-level failure, with a descriptive error message and the raw model text preserved for diagnostics

### Requirement: An English card name is derived for lookup in the English-only catalog
When the detected attributes include a card name, the derived attributes SHOULD include `card_name_en`: the English name of the card, being the official English name if it is known and otherwise a faithful translation of the detected name. A card name that is already English SHALL be repeated unchanged, and character names SHALL be kept as they are. When the detected attributes include no card name, `card_name_en` SHALL be omitted. `card_name_en` SHALL be a scalar string on the same terms as any other derived attribute; its absence SHALL NOT fail derivation.

#### Scenario: German card name
- **WHEN** the detected card name is "Feuer-Drache"
- **THEN** the derived attributes include `card_name_en` with the English name of that card (e.g. "Fire Dragon")

#### Scenario: English card name
- **WHEN** the detected card name is already English
- **THEN** `card_name_en` repeats that name

#### Scenario: No detected card name
- **WHEN** the detected attributes contain no card name
- **THEN** the derived attributes contain no `card_name_en`
