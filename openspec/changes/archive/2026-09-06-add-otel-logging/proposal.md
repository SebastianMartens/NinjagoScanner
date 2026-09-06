## Why

The `add-opentelemetry-observability` change (archived 2026-08-25) wired
distributed tracing and metrics for all three services but explicitly left
logging out of scope, relying on Fly's plain stdout log capture instead. That
gap makes it hard to find `ILogger` output in Grafana Cloud at all — logs
exist only as untagged text in `flyctl logs`, disconnected from the traces
and metrics already flowing there, with no way to pivot from a trace to the
log lines emitted during that same request. This change closes that gap by
exporting logs through the same OTLP pipeline already established for
traces/metrics, so all three telemetry signals land in Grafana Cloud
correlated by trace/span ID.

## What Changes

- Add the OpenTelemetry Logging SDK to all three services
  (`NinjagoScanner.Web`, `NinjagoScanner.CatalogService`,
  `NinjagoScanner.PictureService`), wired via `builder.Logging.AddOpenTelemetry(...)`
  the same way tracing/metrics are wired via `builder.Services.AddOpenTelemetry()`.
- Export logs via OTLP/HTTP-protobuf to Grafana Cloud, reusing the same
  `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_HEADERS` Fly secrets
  already configured for traces/metrics (Grafana Cloud routes by
  `service.name` resource attribute, same as today).
- Keep the existing console logging provider active in addition to the OTLP
  log exporter, so `flyctl logs` keeps working unchanged.
- Non-blocking export: a telemetry backend outage must not affect request
  handling, matching the existing tracing/metrics behavior.

## Capabilities

### Modified Capabilities
- `observability`: adds a "log export" requirement alongside the existing
  tracing and metrics requirements — logs emitted via `ILogger` in any of
  the three services are exported via OTLP to Grafana Cloud and correlated
  with the active trace/span ID when one exists.

## Impact

- `NinjagoScanner.Web/Program.cs`, `NinjagoScanner.CatalogService/Program.cs`,
  `NinjagoScanner.PictureService/Program.cs`: add `builder.Logging.AddOpenTelemetry(...)`.
- `NinjagoScanner.Web.csproj`, `NinjagoScanner.CatalogService.csproj`,
  `NinjagoScanner.PictureService.csproj`: no new package needed —
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` (already referenced) covers
  the logs exporter too.
- No changes to Fly secrets/infrastructure: existing `OTEL_EXPORTER_OTLP_*`
  secrets are reused as-is.
- No application-code changes beyond `Program.cs` — existing `ILogger<T>`
  call sites are unaffected; they simply gain a second sink.
