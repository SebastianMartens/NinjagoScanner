## ADDED Requirements

### Requirement: A failure result indicates whether Gemini evaluated the photo
Every failed analysis result SHALL indicate whether the failure occurred because the Gemini API never produced a response it evaluated the photo with (a transport-level failure — retries exhausted against 429/5xx, an immediate non-retryable HTTP error, or an exception raised while attempting the call) as opposed to a failure derived from a response Gemini did return (a content-level failure — malformed/unusable model output, a model-reported failure, or an unresolved series match).

#### Scenario: Retries exhausted against repeated 429/5xx
- **WHEN** the Gemini API responds with 429 or a 5xx status on every attempt up to the configured maximum
- **THEN** the failure result is marked as a transport-level failure

#### Scenario: Immediate non-retryable HTTP error
- **WHEN** the Gemini API responds with a status other than 429 or 5xx (e.g. 400 or 404)
- **THEN** the failure result is marked as a transport-level failure

#### Scenario: Exception raised while calling the API
- **WHEN** an exception is raised while attempting the Gemini API call, before any response is received
- **THEN** the failure result is marked as a transport-level failure

#### Scenario: Malformed or empty model output
- **WHEN** the Gemini API responds successfully (2xx) but the response contains no usable text, or the text is not valid JSON matching the expected payload shape
- **THEN** the failure result is marked as a content-level failure, not a transport-level failure

#### Scenario: Model reports failed status
- **WHEN** the Gemini API responds successfully (2xx) and the model payload's status is `failed`
- **THEN** the failure result is marked as a content-level failure, not a transport-level failure

#### Scenario: Series-name match escalation
- **WHEN** the Gemini API responds successfully (2xx) and the analysis status is escalated to `failed` because series-name matching found no confident match
- **THEN** the failure result is marked as a content-level failure, not a transport-level failure
