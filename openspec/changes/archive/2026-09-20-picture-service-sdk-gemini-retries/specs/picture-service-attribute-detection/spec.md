## MODIFIED Requirements

### Requirement: Transient failures are retried with increasing delay
If the Gemini API call underlying attribute detection fails with a transient condition (rate limiting, a server error, or a transient connection or timeout error), the call SHALL be retried under the retry policy defined in `picture-service-gemini-analysis`: up to the configured maximum number of attempts in total, waiting an exponentially growing, jittered delay between attempts. A non-transient failure SHALL fail immediately without retrying.

#### Scenario: Rate limited then succeeds
- **WHEN** the underlying call is rate-limited on an early attempt and succeeds on a later attempt within the configured attempt limit
- **THEN** the call is retried after an exponentially growing, jittered wait and the eventual successful response is used

#### Scenario: Retries exhausted
- **WHEN** the underlying call fails with a transient condition on every attempt up to the configured maximum
- **THEN** attribute detection fails with a transport-level failure and no further attempts are made

#### Scenario: Non-retryable failure
- **WHEN** the underlying call fails with a condition that is not transient
- **THEN** attribute detection fails immediately with a transport-level failure, without retrying
