## MODIFIED Requirements

### Requirement: Other scan settings follow the same request-then-config-then-default precedence
Scanner settings other than the directory (API key, model, catalog service address, overwrite flag, delay, max attempts, timeout) SHALL each be resolved using a per-request value first, then a named configuration key or environment variable, then a hard-coded default. No setting controls the delay between retries of a Gemini call; a `retry_delay_ms` value supplied on a request SHALL be ignored.

#### Scenario: Request-provided model overrides configuration
- **WHEN** a `Scan` request specifies a model name
- **THEN** that model is used instead of any configured `Gemini:Model`/`GEMINI_MODEL` value or the built-in default

#### Scenario: Max attempts and timeout have enforced minimums
- **WHEN** the resolved `max_attempts` or `timeout_seconds` value (from request, configuration, or default) is below the enforced minimum (1 attempt, 10 seconds)
- **THEN** the enforced minimum is used instead of the lower resolved value

#### Scenario: Request-provided retry delay is ignored
- **WHEN** a `Scan` or `UploadPhoto` request supplies `retry_delay_ms`
- **THEN** the request is processed normally and the value has no effect on the wait between retries
