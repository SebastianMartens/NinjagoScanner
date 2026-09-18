# picture-service-gemini-analysis Specification

## Purpose

Defines the contract of the outbound call to the Gemini API used to analyze a single card photo: what is sent, how transient failures are retried, and how the response is turned into an analysis result.

## Requirements

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
