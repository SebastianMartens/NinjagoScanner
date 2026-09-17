## Why

`add-otel-logging` (archived 2026-09-06) wired OTLP log export into all
three services, but at the time all three were .NET and the change wired
it via `builder.Logging.AddOpenTelemetry(...)`. `picture_service/` (the
Python rewrite of PictureService, see `picture-service-python-rewrite`)
only ported tracing (`main.py`'s `_configure_tracing`) — task 7.1 there
explicitly scoped out logging and metrics. Right now `picture_service`'s
log output (`logging.basicConfig(level=logging.INFO)`) only reaches
per-machine stdout capture (`flyctl logs`), unlike CatalogService, Web,
and the still-deployed C# PictureService, whose logs already land in
Grafana Cloud correlated with their traces. This closes that gap so the
Python implementation meets the same `observability` requirement before
it takes over as the deployed PictureService.

## What Changes

- Configure a Python OpenTelemetry `LoggerProvider` in `picture_service/src/picture_service/main.py`,
  exporting via `opentelemetry.exporter.otlp.proto.http._log_exporter.OTLPLogExporter`
  (same `-proto-http`-specific package already used for tracing, so the
  protocol is unambiguously HTTP/protobuf — see the existing
  `_configure_tracing` docstring's reasoning about Grafana Cloud's gateway).
- Attach that provider to the stdlib `logging` module via OpenTelemetry's
  `LoggingHandler`, added alongside the existing `logging.basicConfig`
  stream handler (both stay active, matching the C# services keeping
  their console provider next to the OTLP one).
- Reuse the existing `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_HEADERS`
  env vars and the `ninjago-scanner-picture-service` resource
  `service.name` already used for tracing — no new configuration surface.
- Non-blocking export, matching tracing: a telemetry backend outage must
  not affect request handling.
- Generalize the `observability` spec's log-export and log-correlation
  requirements, which currently reference `ILogger` (a .NET-only API), to
  describe the behavior in language/runtime-neutral terms so they
  correctly cover a Python service's `logging` module too.

## Capabilities

### Modified Capabilities
- `observability`: the "Log export to observability backend" and "Log
  correlation with trace context" requirements drop their `ILogger`-specific
  wording and add scenarios confirming `picture_service`'s Python
  `logging`-module output is exported via OTLP and correlated with active
  trace/span IDs the same way the .NET services' `ILogger` output already is.

## Impact

- `picture_service/src/picture_service/main.py`: add a
  `_configure_logging()` alongside `_configure_tracing()`, called from
  `_serve()`.
- `picture_service/pyproject.toml`: no new dependency —
  `opentelemetry-exporter-otlp-proto-http` (already referenced) provides
  the log exporter too.
- No Fly secrets/infrastructure changes: existing `OTEL_EXPORTER_OTLP_*`
  secrets are reused as-is.
- No changes to `NinjagoScanner.Web`, `NinjagoScanner.CatalogService`, or
  the C# `NinjagoScanner.PictureService` — they already export logs via
  OTLP per the archived `add-otel-logging` change.
