## MODIFIED Requirements

### Requirement: Transient HTTP failures are retried with increasing delay
If a Gemini API call in the analysis pipeline responds with HTTP 429 or a 5xx status, that call SHALL be retried up to the configured maximum number of attempts, waiting `retry_delay_ms * attempt` between attempts. Any other non-success status SHALL fail immediately without retrying. This policy applies to each Gemini call in the pipeline individually (see `picture-service-attribute-detection` and `picture-service-derived-attributes`), not to the pipeline as a whole.

#### Scenario: Rate limited then succeeds
- **WHEN** a Gemini API call responds with 429 on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after waiting `retry_delay_ms * attempt` and the eventual successful response is used

#### Scenario: Server error exhausts all attempts
- **WHEN** a Gemini API call responds with a 5xx status on every attempt up to the configured maximum
- **THEN** that call's result is a failure describing the API error, and no further attempts are made after the limit

#### Scenario: Non-retryable error
- **WHEN** a Gemini API call responds with a status other than 429 or 5xx (e.g. 400 or 404)
- **THEN** that call fails immediately on the first such response without retrying

### Requirement: A failure result indicates whether Gemini evaluated the input
Every failed result from a Gemini call in the pipeline SHALL indicate whether the failure occurred because Gemini never produced a response it evaluated the input with (a transport-level failure — retries exhausted against 429/5xx, an immediate non-retryable HTTP error, or an exception raised while attempting the call) as opposed to a failure derived from a response Gemini did return (a content-level failure — malformed/unusable model output, or a model-reported failure).

#### Scenario: Retries exhausted against repeated 429/5xx
- **WHEN** a Gemini API call responds with 429 or a 5xx status on every attempt up to the configured maximum
- **THEN** that call's failure result is marked as a transport-level failure

#### Scenario: Immediate non-retryable HTTP error
- **WHEN** a Gemini API call responds with a status other than 429 or 5xx (e.g. 400 or 404)
- **THEN** that call's failure result is marked as a transport-level failure

#### Scenario: Exception raised while calling the API
- **WHEN** an exception is raised while attempting a Gemini API call, before any response is received
- **THEN** that call's failure result is marked as a transport-level failure

#### Scenario: Malformed or empty model output
- **WHEN** a Gemini API call responds successfully (2xx) but the response contains no usable text, or the text is not valid JSON matching the expected payload shape
- **THEN** that call's failure result is marked as a content-level failure, not a transport-level failure

#### Scenario: Model reports failed status
- **WHEN** a Gemini API call responds successfully (2xx) and the model payload's status is `failed`
- **THEN** that call's failure result is marked as a content-level failure, not a transport-level failure

### Requirement: Malformed or empty model output is treated as a failure
If a Gemini API call in the pipeline succeeds but the response contains no usable text, or the text is not valid JSON matching that call's expected payload shape, the result SHALL be a failure with a descriptive error message, not a crash or a successful result.

#### Scenario: Empty candidate text
- **WHEN** a Gemini API call responds successfully but no candidate/part contains non-empty text
- **THEN** that call's result is a failure with a message stating no JSON result was returned

#### Scenario: Invalid JSON in model output
- **WHEN** the extracted model text cannot be parsed as that call's expected JSON payload
- **THEN** that call's result is a failure with a message describing the JSON parse failure, and the raw model text is preserved for diagnostics

## REMOVED Requirements

### Requirement: Analysis request includes the photo and a series-catalog prompt
**Reason**: Sending the photo together with a series-catalog prompt was specific to the single one-shot call. The staged pipeline's photo-carrying call (attribute detection) deliberately excludes the series catalog - see `picture-service-attribute-detection`'s "Attribute detection request includes only the photo".
**Migration**: See `picture-service-attribute-detection`.

### Requirement: Confidence is clamped and status is normalized from the model payload
**Reason**: A single confidence/status value derived from one model payload no longer exists - the pipeline now has two separate model calls plus a deterministic matching stage, and the Judged analysis status is computed by combining all three (see `picture-service-catalog-matching`'s "Judged analysis status reflects detection, derivation, and matching outcomes"). The exact confidence handling within the new pipeline is intentionally left open - see this change's design.md.
**Migration**: See `picture-service-catalog-matching`.

### Requirement: Resolved set name is discarded for failed analyses
**Reason**: Set-name (series) resolution now happens in the catalog-matching stage, after both model calls, not immediately after a single call's payload is parsed.
**Migration**: See `picture-service-catalog-matching`.

### Requirement: A failed series-name match escalates the analysis status and preserves the raw guess
**Reason**: Generalized to cover card number as well as series, and reframed around the three-stage pipeline's outcomes rather than a single call's payload.
**Migration**: See `picture-service-catalog-matching`'s "Judged analysis status reflects detection, derivation, and matching outcomes" and "Unresolved matches preserve the raw guess rather than storing nothing".

### Requirement: Analysis result includes a detected language
**Reason**: Language is no longer parsed directly off one call's fixed payload shape now that call returns a generic key-value map. Whether language detection belongs to attribute detection or derived attributes is deliberately left open by this change - see design.md's Open Questions.
**Migration**: None yet - tracked as an open question rather than resolved into another capability.
