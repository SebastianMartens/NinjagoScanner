## Why

`picture_service` runs its own retry loop (`_invoke_with_retry` in `gemini_service.py`) around every Gemini call, while the Gemini SDK underneath already retries the same conditions (408/429/5xx plus transient connection/timeout errors) with exponential backoff and jitter. The two stacked: LangChain's default of 6 SDK retries sat under our 3 attempts, so one rate-limited call could make up to 18 requests with no trace visibility, and the SDK's timeout was never set. Keeping a second, weaker (linear, no jitter) implementation of what the SDK already does is duplicated logic with no benefit; one policy, configured through the SDK, is simpler and easier to observe.

## What Changes

- Remove `_invoke_with_retry`'s retry loop and its `asyncio.sleep` backoff; each stage makes a single `ainvoke` call and the SDK performs any retries.
- Configure the SDK's retry from the existing scan settings: `max_attempts` becomes the SDK's total attempt count (including the original request) and `timeout_seconds` bounds each individual HTTP attempt.
- **BREAKING (behavioral, not wire-level)**: the wait between attempts changes from linear (`retry_delay_ms * attempt`) to the SDK's exponential-with-jitter backoff (about 1 s, doubling, capped at 60 s); retryable conditions become the SDK's (HTTP 408, 429, 5xx, plus transient connection/timeout errors) instead of 429/5xx only.
- **BREAKING**: `retry_delay_ms` no longer has any effect and is retired: the SDK's supported configuration path (through LangChain) exposes only the attempt count and timeout, not the backoff parameters. The `RETRY_DELAY_MS` environment variable and `ScannerConfig.retry_delay_ms` are removed. The `retry_delay_ms` fields on `ScanRequest` and `UploadPhotoMetadata` are kept for wire compatibility but marked deprecated and ignored (no field renumbering, no Web change - the Web app never sets them).
- Failure classification is unchanged: a Gemini call that still fails after the SDK gives up, or fails with a non-retryable error, is a transport-level failure with the same messages as today.
- Drop the per-attempt bookkeeping in the tracing added for diagnosis (`gemini.retry_backoff` span, `gemini.attempt`/`gemini.retryable` attributes); one `gemini.invoke` span per Gemini call remains, and the individual HTTP requests (one span each, including SDK retries) stay visible through the aiohttp/httpx client instrumentation.

## Capabilities

### New Capabilities
<!-- None -->

### Modified Capabilities
- `picture-service-gemini-analysis`: the retry requirement changes from a service-owned `retry_delay_ms * attempt` policy on 429/5xx to the SDK's retry policy configured from `max_attempts`/`timeout_seconds`; transport-failure scenarios reworded to match.
- `picture-service-attribute-detection`: the retry requirement is restated the same way for stage 1.
- `picture-service-derived-attributes`: the retry requirement is restated the same way for stage 2.
- `picture-service-directory-resolution`: `retry delay` is removed from the list of settings resolved request-then-config-then-default.

## Impact

- Code: `picture_service/src/picture_service/gemini_service.py` (`build_chat_model`, `_invoke_with_retry`), `config.py` (`retry_delay_ms` and `RETRY_DELAY_MS` removed), `picture_scanner_service.py` (stops passing `retry_delay_ms` to config), `picture_service/tests/` (`test_gemini_service.py` tests that exercise our retry loop with fake models are replaced by tests of the model construction; `test_config.py` loses its retry-delay cases).
- Dependencies: none.
- APIs: `picture_service.proto` (canonical copy in `NinjagoScanner.Web/Protos/`, which picture_service's codegen reads): two `retry_delay_ms` fields gain `[deprecated = true]` and a comment. Wire format unchanged; `NinjagoScanner.Web` needs no code change beyond regenerating stubs.
- Operations: with Gemini rate limiting, the observed backoff schedule differs from before, and any deployed `RETRY_DELAY_MS` secret/env var becomes inert (nothing in the repo's `fly.toml` files sets it; harmless if set as a Fly secret).
