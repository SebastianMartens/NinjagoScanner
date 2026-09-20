## 1. Config

- [x] 1.1 Remove `retry_delay_ms` from `ScannerConfig`, both loaders, `_DEFAULT_RETRY_DELAY_MS`, and the `RETRY_DELAY_MS` env var in `picture_service/src/picture_service/config.py`
- [x] 1.2 Stop passing `retry_delay_ms` from `ScanRequest`/`UploadPhotoMetadata` in `picture_scanner_service.py` (`Scan` and `UploadPhoto`)
- [x] 1.3 Update `picture_service/tests/test_config.py`: drop the retry-delay assertions and the "request overrides env" retry-delay test

## 2. Gemini service

- [x] 2.1 In `build_chat_model`, pass `max_retries=config.max_attempts` and keep `timeout=config.timeout_seconds`; update the comment to say the SDK owns retries and `max_attempts` is the total attempt count
- [x] 2.2 Reduce `_invoke_with_retry` to a single `ainvoke` (rename it to reflect that it no longer retries); keep mapping `ModelError` to `_failure_message`, and remove the loop, `asyncio.sleep` and `is_retryable` handling
- [x] 2.3 Update the module and function docstrings in `gemini_service.py` that describe the retry policy (`retry_delay_ms * attempt` backoff, "LangChain's own generic retry isn't used")
- [x] 2.4 Trim the tracing: keep the `gemini.invoke` span (with `gemini.model`), remove the `gemini.retry_backoff` span and the `gemini.attempt`/`gemini.retryable` attributes
- [x] 2.5 Update `picture_service/tests/test_gemini_service.py`: replace the tests that drive our retry loop with (a) a test that a retryable `ModelError` yields a transport-failure result after exactly one `ainvoke`, and (b) a test that `build_chat_model` builds the model with `max_retries == max_attempts` and `timeout == timeout_seconds`

## 3. Proto

- [x] 3.1 In `NinjagoScanner.Web/Protos/picture_service.proto`, mark `ScanRequest.retry_delay_ms` and `UploadPhotoMetadata.retry_delay_ms` with `[deprecated = true]` and a comment that the value is ignored
- [x] 3.2 Regenerate picture_service stubs (`uv run python scripts/gen_proto.py`) and confirm `dotnet build NinjagoScanner.slnx` still builds without new warnings

## 4. Verify

- [x] 4.1 `uv run pytest` in `picture_service/` and `dotnet test NinjagoScanner.slnx` pass
- [x] 4.2 Run a real `Scan` against Gemini and confirm in the trace that each stage has one `gemini.invoke` span and that any retry appears as multiple HTTP client spans under it
- [x] 4.3 Update `openspec/GLOSSARY.md` only if a retry-related term is defined there (grep for "retry" found nothing, so no edit needed)
