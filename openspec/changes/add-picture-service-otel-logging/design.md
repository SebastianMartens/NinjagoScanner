## Context

`picture_service/src/picture_service/main.py` already configures tracing
in `_configure_tracing()`: a `TracerProvider` with resource
`service.name=ninjago-scanner-picture-service`, a `BatchSpanProcessor`
wrapping `OTLPSpanExporter` from `opentelemetry.exporter.otlp.proto.http.trace_exporter`
(the HTTP/protobuf-specific exporter package, not the combined
protocol-switchable one), and `GrpcAioInstrumentorServer().instrument()`
for span creation on inbound RPCs. Logging today is just
`logging.basicConfig(level=logging.INFO)` in `_serve()` — stdout only.

The three .NET services already export logs the same way traces/metrics
go out: `builder.Services.AddOpenTelemetry().WithLogging(logging => logging
.AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf),
options => options.IncludeFormattedMessage = true)` (see
`NinjagoScanner.CatalogService/Program.cs`), reusing the same
`OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` Fly secrets and
`service.name` resource as tracing. `.NET`'s OTLP logging bridge
automatically stamps the active `Activity`'s trace/span IDs onto exported
log records when one is active. See proposal.md - Why for why this
matters (pivoting from a trace to its logs in Grafana Cloud).

`opentelemetry-exporter-otlp-proto-http` (already a `picture_service`
dependency for tracing) also ships the log exporter — confirmed in the
installed venv: `opentelemetry.sdk._logs.LoggerProvider`/`LoggingHandler`,
`opentelemetry.sdk._logs.export.BatchLogRecordProcessor`, and
`opentelemetry.exporter.otlp.proto.http._log_exporter.OTLPLogExporter` all
import cleanly. No new dependency is needed.

## Goals / Non-Goals

**Goals:**
- `picture_service` log output reaches the same OTLP backend as its
  traces, correlated by trace/span ID when a trace is active, matching
  what `observability`'s log-export/log-correlation requirements already
  demand of the other two services.
- Keep stdout logging working unchanged (`flyctl logs` stays useful).
- Non-blocking export, matching the existing tracing behavior.

**Non-Goals:**
- Metrics for `picture_service` — still out of scope (tracked separately;
  the C# services' `WithMetrics(...)` has no Python counterpart yet).
- Changing anything in the .NET services — they already do this.
- Structured/JSON log formatting changes to the stdout sink.
- Adjusting log levels or call sites — this only adds a second sink for
  whatever the service already logs.

## Decisions

**Wire logging via the stdlib `logging` module's handler mechanism, not a
custom logger.** OpenTelemetry's Python logs SDK integrates with `logging`
by attaching an `opentelemetry.sdk._logs.LoggingHandler` (backed by a
`LoggerProvider`) to a logger, the same way `logging.basicConfig` attaches
a `StreamHandler`. Attaching it to the root logger (`logging.getLogger()`)
alongside the existing `basicConfig` handler means every existing
`logger.info(...)`/`logger.warning(...)` call site in the codebase (not
just `logging.getLogger("picture_service")`) gains OTLP export for free,
with no call-site changes — mirroring the .NET services, where
`WithLogging(...)` instruments every `ILogger<T>` without call-site
changes.

**Reuse the `-proto-http`-specific exporter package, not the combined
one.** Same reasoning as `_configure_tracing`'s existing docstring: the
combined `opentelemetry-exporter-otlp-proto` package picks gRPC by default
via `OTEL_EXPORTER_OTLP_PROTOCOL`, which Grafana Cloud's OTLP gateway
doesn't accept. Depending on `opentelemetry.exporter.otlp.proto.http._log_exporter.OTLPLogExporter`
specifically makes the protocol HTTP/protobuf by construction, not by
relying on an env var default staying correct.

**Trace correlation comes for free from the SDK, not custom code.**
`LoggingHandler` reads the current OTel context (populated by
`GrpcAioInstrumentorServer`'s spans around inbound RPCs) when emitting
each `LogRecord`, attaching `trace_id`/`span_id` automatically when one is
active, and omitting them otherwise. No manual context propagation needed
— same as the .NET `WithLogging` bridge.

**Reuse the same `Resource`/service name as tracing**, rather than
constructing a second `Resource.create(...)` — a shared `Resource` object
is built once in `_configure_tracing`'s style and passed to both the
`TracerProvider` and the new `LoggerProvider`, so `service.name` can never
drift between the two signals for this service.

**Alternative considered**: emit logs only via span events on the active
span instead of a separate logs pipeline. Rejected — span events aren't
queryable independently of a trace (violates the same reasoning as the
existing "Service-level performance metrics" requirement being separate
from tracing), and startup-time logs (before any span exists) would be
lost entirely.

## Risks / Trade-offs

- [Log volume/cost] Every log line now also goes over the network to the
  OTLP backend → `BatchLogRecordProcessor` batches and drops if the
  backend is unreachable, matching the existing non-blocking tracing
  behavior and the `observability` capability's "Non-blocking telemetry
  export" requirement; no request-path impact.
- [`IncludeFormattedMessage` parity] The .NET services set
  `options.IncludeFormattedMessage = true` so the human-readable message
  (not just the raw template + args) is exported. `LoggingHandler` exports
  the formatted `record.getMessage()` by default, so no equivalent flag is
  needed in Python — noted here so a future reader doesn't go looking for
  one.
- [Root vs. named logger] Attaching the handler to the root logger will
  also export logs from dependencies (e.g. `grpc`, `aioboto3`) if they log
  at INFO+; acceptable since `logging.basicConfig(level=logging.INFO)`
  already applies the same root-logger scope to stdout today, so this
  doesn't change what's captured, only where it also goes.

## Migration Plan

- Additive, single-service change with no data migration. Deploy
  `picture_service` with `_configure_logging()` wired in; existing
  `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` Fly secrets
  (already set for tracing) apply immediately, no new secrets needed.
- Rollback: revert the deploy; stdout logging (`flyctl logs`) is
  unaffected either way since it stays wired throughout.
- Verify in Grafana Cloud the same way tracing was verified during the
  Python rewrite (`picture-service-python-rewrite` tasks 7.1/7.2): confirm
  locally that a log line is POSTed as OTLP/HTTP protobuf to `/v1/logs`,
  then confirm arrival in the real Grafana Cloud account post-deploy
  (production OTLP secrets aren't available in a local dev session).
