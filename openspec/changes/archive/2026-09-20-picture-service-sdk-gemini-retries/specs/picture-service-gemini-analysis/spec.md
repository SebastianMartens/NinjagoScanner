## MODIFIED Requirements

### Requirement: Transient HTTP failures are retried with increasing delay
If a Gemini API call in the analysis pipeline fails with a transient condition — HTTP 408, 429 or a 5xx status, or a transient connection or timeout error — that call SHALL be retried until it has been attempted the configured maximum number of times in total (the original request counts as the first attempt). The wait before each retry SHALL grow exponentially with each further retry, with random jitter, starting at about 1 second and capped at 60 seconds; the delay is not configurable. Each individual attempt SHALL be bounded by the configured `timeout_seconds`. Any other failure SHALL fail immediately without retrying. This policy applies to each Gemini call in the pipeline individually (see `picture-service-attribute-detection` and `picture-service-derived-attributes`), not to the pipeline as a whole, and is the only retry policy applied to a call: attempts are not multiplied by any additional retry layer.

#### Scenario: Rate limited then succeeds
- **WHEN** a Gemini API call responds with 429 on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after an exponentially growing, jittered wait, and the eventual successful response is used

#### Scenario: Server error exhausts all attempts
- **WHEN** a Gemini API call responds with a 5xx status on every attempt up to the configured maximum
- **THEN** that call's result is a failure describing the API error, and no further attempts are made after the limit

#### Scenario: Attempt limit counts the original request
- **WHEN** `max_attempts` is 3 and every attempt fails with a transient condition
- **THEN** exactly 3 requests are made in total (the original plus 2 retries)

#### Scenario: A hung attempt times out
- **WHEN** a Gemini API call receives no response within `timeout_seconds`
- **THEN** that attempt is abandoned and, if attempts remain, retried like any other transient failure

#### Scenario: Non-retryable error
- **WHEN** a Gemini API call responds with a status that is not transient (e.g. 400 or 404)
- **THEN** that call fails immediately on the first such response without retrying

### Requirement: A failure result indicates whether Gemini evaluated the input
Every failed result from a Gemini call in the pipeline SHALL indicate whether the failure occurred because Gemini never produced a response it evaluated the input with (a transport-level failure — retries exhausted against transient failures, an immediate non-retryable HTTP error, or an exception raised while attempting the call) as opposed to a failure derived from a response Gemini did return (a content-level failure — malformed/unusable model output, or a model-reported failure).

#### Scenario: Retries exhausted against repeated 429/5xx
- **WHEN** a Gemini API call fails with a transient condition (429, a 5xx status, or a timeout) on every attempt up to the configured maximum
- **THEN** that call's failure result is marked as a transport-level failure

#### Scenario: Immediate non-retryable HTTP error
- **WHEN** a Gemini API call responds with a status that is not transient (e.g. 400 or 404)
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
