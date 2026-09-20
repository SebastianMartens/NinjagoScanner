## Context

See proposal.md for motivation. Current state in `picture_service`:

- `gemini_service.build_chat_model` builds a `ChatGoogleGenerativeAI` with the library default `max_retries=6` (and, as of the tracing work, `max_retries=1` plus `timeout`); `_invoke_with_retry` wraps each stage's `ainvoke` in a loop of `max_attempts` with a linear `retry_delay_ms * attempt` sleep, catching `langchain_core.exceptions.ModelError` and using its `is_retryable` flag.
- The Gemini SDK (`google-genai`) retries 408/429/5xx and transient httpx/connect errors itself (tenacity, exponential backoff with jitter, `attempts` counting the original request). LangChain exposes this only as the `max_retries` field (sets `attempts`) and `timeout`; the backoff parameters (`initial_delay`, `max_delay`, `exp_base`, `jitter`) live on the SDK's `HttpRetryOptions` and have no constructor-level path through LangChain.
- Stage failures are turned into `AttributeStageResult(is_transport_failure=True)` (from a caught `ModelError`) or propagate as an unhandled exception to the RPC handler, which turns them into a transport-failure result (`Unerwarteter Fehler: ...`). `Scan` stops early on either.

## Goals / Non-Goals

**Goals:**
- Exactly one retry layer between the service and Gemini, owned by the SDK.
- `max_attempts` and `timeout_seconds` keep their meaning and enforced minimums.
- Failure classification (transport vs. content) is unchanged.

**Non-Goals:**
- Honoring the server-suggested `retry_delay` on 429 (the SDK ignores it, and so did the old loop).
- Changing `delay_between_requests_ms`, concurrency, or model/prompt choice.
- Removing the `retry_delay_ms` proto fields (wire compatibility; only deprecated).

## Decisions

**1. Configure via LangChain's own `max_retries` and `timeout` fields.** `build_chat_model` passes `max_retries=config.max_attempts` and `timeout=config.timeout_seconds`. `max_attempts >= 1` is already enforced by `ScannerConfig`, which sidesteps the SDK quirk where `0` means "use the default (5)"; `1` means no retries.
- *Alternative: pass a full `HttpRetryOptions` (attempts + `initial_delay`) as a per-request `http_options` kwarg.* Rejected: a spike showed `with_structured_output` rejects extra kwargs, and `http_options` bound on the resulting runnable never reaches the request (the request still carried `attempts=1`). Making it work needs a subclass overriding private LangChain hooks (`_generate`/`_build_request_config`), which is the kind of coupling this change removes.
- *Alternative: mutate the SDK client's internal `retry_options` after construction.* Rejected: reaches into private state of two libraries.

**2. Retire `retry_delay_ms` rather than keep an inert setting.** Since the backoff shape can't be configured through the supported path (decision 1), `ScannerConfig.retry_delay_ms`, its `RETRY_DELAY_MS` env var and its default are removed, and the two proto fields get `[deprecated = true]` plus a comment saying they are ignored. Removing (or `reserved`-ing) the fields would force a coordinated Web/PictureService proto change for no benefit; the Web app never sets them.
- *Alternative: keep the config but ignore it.* Rejected: dead configuration that suggests a knob that doesn't turn.

**3. Single `ainvoke` per stage; keep `ModelError` classification.** `_invoke_with_retry` becomes a single call that maps a `ModelError` to `_failure_message(...)` (keeping the "model no longer available" hint) and returns the same `_InvokeOutcome`; the retry loop, `asyncio.sleep` and `is_retryable` check go away. Exceptions that are not `ModelError` (for example a raw timeout after the SDK exhausted its attempts, or a 408 that LangChain does not map to a `ModelError`) propagate to the RPC handler exactly as non-`ModelError` exceptions did before, so they are still transport-level failures.
- *Alternative: also catch `ChatGoogleGenerativeAIError` to give those a "Gemini API Fehler" message.* Deferred: it only changes the message text of a rare case and can be a follow-up if traces show it matters.

**4. Tracing stays, minus per-attempt bookkeeping.** Keep one `gemini.invoke` span per call (wrapping the SDK's retries) with `gemini.model`. The aiohttp/httpx client instrumentation already produces one span per HTTP request, which is where individual SDK retries show up. Remove the `gemini.retry_backoff` span and the `gemini.attempt`/`gemini.retryable` attributes, which only made sense with our loop.

## Risks / Trade-offs

- **Shorter waits with the default `max_attempts=3`** (SDK backoff is about 1 s then 2 s, versus 3 s then 6 s before) → on a tight rate limit a call may exhaust its attempts sooner. Mitigation: `MAX_ATTEMPTS` can be raised via env with no code change (5 attempts wait about 1+2+4+8 s); check the new traces after deploy.
- **Broader retry set** (408 and transient connection/timeout errors are now retried; previously only 429/5xx) → slightly longer worst-case latency for a dead connection, bounded by `max_attempts * timeout_seconds`. Accepted; this is what the SDK considers safe to retry.
- **Two libraries decide error mapping** → if a LangChain upgrade changes which status codes become `ModelError`, message text may shift, but transport-vs-content classification is preserved since unmapped exceptions still propagate to the handler.
- **Retry behaviour is no longer unit-testable with fakes** → tests can only assert the model is built with the right `max_retries`/`timeout` and that a stage calls the model exactly once; the SDK's own retry behaviour is not re-tested here.

## Migration Plan

1. Regenerate stubs in `picture_service` (`uv run python scripts/gen_proto.py`) and rebuild Web (`dotnet build`) after the proto comment/deprecation change. The wire format is unchanged, so services can deploy in any order.
2. Deploy `picture_service` to Fly. Nothing in the repo's `fly.toml` files sets `RETRY_DELAY_MS`; if it exists as a Fly secret it becomes inert.
3. Rollback: revert the change; no data or wire-format migration is involved.
