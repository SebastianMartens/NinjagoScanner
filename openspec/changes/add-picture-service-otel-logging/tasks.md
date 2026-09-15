## 1. Logging configuration

- [x] 1.1 In `picture_service/src/picture_service/main.py`, extract the shared
  `Resource.create({SERVICE_NAME: _SERVICE_NAME})` out of `_configure_tracing()`
  into a module-level helper (or local var built once in `_serve()` and
  passed to both configurators) and verify `_configure_tracing()`'s
  existing behavior is unchanged (tracing still exports as before).
- [x] 1.2 Add `_configure_logging()` to `main.py`: build a
  `LoggerProvider(resource=...)` (`opentelemetry.sdk._logs`), add a
  `BatchLogRecordProcessor(OTLPLogExporter())` (processor from
  `opentelemetry.sdk._logs.export`, exporter from
  `opentelemetry.exporter.otlp.proto.http._log_exporter` — the HTTP/protobuf-
  specific package, matching `_configure_tracing`'s exporter choice), attach
  an `opentelemetry.sdk._logs.LoggingHandler(logger_provider=provider)` to
  the root logger via `logging.getLogger().addHandler(...)`, and call it
  from `_serve()` alongside `_configure_tracing()`. Verify `picture_service`
  still starts locally and existing log lines (e.g. "PictureService
  listening on port %s") still print to stdout as before.
- [x] 1.3 Verify OTLP export locally: point `OTEL_EXPORTER_OTLP_ENDPOINT` at
  a throwaway local HTTP listener (or reuse the approach from
  `picture-service-python-rewrite` task 7.2) and confirm a log line is
  POSTed as `Content-Type: application/x-protobuf` to `/v1/logs`.
- [x] 1.4 Verify trace correlation locally: trigger a logged line from
  inside a gRPC handler while a span is active (e.g. add a temporary log
  call in `picture_scanner_service.py`, or exercise an existing one) and
  confirm the exported `LogRecord` carries a non-empty trace ID/span ID
  matching the concurrently exported span; then confirm a startup-time log
  (before any span exists) exports with no trace/span ID.
- [x] 1.5 Confirm non-blocking export: point `OTEL_EXPORTER_OTLP_ENDPOINT`
  at an unreachable address and verify the service still starts, serves a
  `Scan`/`ListCards` call, and logs normally to stdout without delay or
  error surfaced to the caller.

## 2. Verification against production backend

- [ ] 2.1 After deploying `picture_service` to Fly with the existing
  `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` secrets,
  confirm log entries for `service.name=ninjago-scanner-picture-service`
  arrive in Grafana Cloud and can be pivoted to/from a concurrent trace by
  trace ID (mirrors `picture-service-python-rewrite` tasks 7.1/7.2 and
  12.2's bake-in monitoring for tracing).

## 3. Spec sync

- [ ] 3.1 Run `openspec archive add-picture-service-otel-logging` once
  deployed and verified, folding the `observability` delta spec's
  generalized (non-`ILogger`-specific) requirement wording into
  `openspec/specs/observability/spec.md`.
