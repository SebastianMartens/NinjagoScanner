## Purpose

Defines the first stage of card analysis: a Gemini vision call that turns a card photo into a generic, open-ended set of detected visual attributes, without any series or catalog reasoning.

## ADDED Requirements

### Requirement: Attribute detection request includes only the photo
Each attribute-detection request SHALL send the card photo as inline image data with the correct MIME type for the file extension, together with a prompt instructing the model to describe what is visible (numbers at their positions, text, symbols, colors, size/aspect cues) as key-value attributes. The request SHALL NOT include the series catalog or ask the model to identify a series, category, or card number against catalog data.

#### Scenario: Request built for an image
- **WHEN** a photo is analyzed
- **THEN** the request sent to the Gemini API contains the image bytes as inline data with the correct MIME type, and a prompt that does not enumerate catalog series

### Requirement: Detected attributes are a generic, flat key-value map
The parsed result of a successful attribute-detection call SHALL be a map of string keys to scalar values (string, number, or boolean). The set of keys is not fixed by this contract - callers and downstream stages interpret key presence and meaning by convention, not by a required schema.

#### Scenario: Successful detection produces a key-value map
- **WHEN** the Gemini API returns a successful, parseable response
- **THEN** the attribute-detection result is a flat map of keys to scalar values, with no required or forbidden key names enforced by this stage

#### Scenario: A value is not a nested structure
- **WHEN** the model's response includes a value that is an object or array rather than a scalar
- **THEN** that value is not accepted as-is into the detected-attributes map (it is dropped or the response is treated as malformed, per the implementation's parsing rules)

### Requirement: Transient failures are retried with increasing delay
If the Gemini API call underlying attribute detection fails with a retryable condition (rate limiting or a server error), the call SHALL be retried up to the configured maximum number of attempts, waiting `retry_delay_ms * attempt` between attempts. A non-retryable failure SHALL fail immediately without retrying.

#### Scenario: Rate limited then succeeds
- **WHEN** the underlying call is rate-limited on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after waiting `retry_delay_ms * attempt` and the eventual successful response is used

#### Scenario: Retries exhausted
- **WHEN** the underlying call fails with a retryable condition on every attempt up to the configured maximum
- **THEN** attribute detection fails with a transport-level failure and no further attempts are made

#### Scenario: Non-retryable failure
- **WHEN** the underlying call fails with a condition that is not retryable
- **THEN** attribute detection fails immediately with a transport-level failure, without retrying

### Requirement: A failure result indicates whether Gemini evaluated the photo
Every failed attribute-detection result SHALL indicate whether the failure is transport-level (Gemini never produced a response it evaluated the photo with) or content-level (Gemini returned a response, but it was unusable or malformed).

#### Scenario: Exception raised while calling the API
- **WHEN** an exception is raised while attempting the call, before any response is received
- **THEN** the failure result is marked as a transport-level failure

#### Scenario: Malformed or empty model output
- **WHEN** the call succeeds but the response contains no usable text, or the text cannot be parsed into a flat key-value map
- **THEN** the failure result is marked as a content-level failure, with a descriptive error message and the raw model text preserved for diagnostics
