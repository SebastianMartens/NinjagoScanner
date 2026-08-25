## Why

Most pages in the app are reported slow, but nothing today can show where the
time actually goes. All three services log only through default `ILogger` to
console (captured as plain Fly text logs) — there are no traces and no
metrics, so a slow `/review` or `/collection` load can't be attributed to
Blazor render cost, gRPC call overhead, DynamoDB reads, S3 calls, or a Fly
cold start without guessing. Two follow-up perf-fix changes are already
planned (removing duplicate Blazor prerender data-fetching, and reusing gRPC
channels instead of opening one per call) and both need real before/after
trace data to demonstrate their impact — so distributed tracing needs to
exist before either of those changes can prove anything.

## What Changes

- Add the OpenTelemetry SDK (traces + metrics) to all three services
  (`NinjagoScanner.Web`, `NinjagoScanner.CatalogService`,
  `NinjagoScanner.PictureService`) via `AddOpenTelemetry()`.
- Instrument ASP.NET Core server-side handling (gRPC/HTTP) and outbound
  `HttpClient` calls (covers the AWS SDK calls PictureService makes to S3/
  DynamoDB) in all three services.
- Instrument the gRPC clients in `NinjagoScanner.Web`
  (`CatalogServiceClient`, `PictureServiceClient`) so W3C trace context
  propagates over gRPC metadata from Web through to CatalogService/
  PictureService — a single browser request produces one end-to-end trace
  spanning every service it touches.
- Export traces and metrics via OTLP directly to Grafana Cloud (Tempo for
  traces, Mimir for metrics) — no self-hosted collector/Alloy — configured
  via Fly secrets (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_HEADERS`)
  per service.
- Explicitly out of scope: fixing the perf issues the exploration already
  found (duplicate Blazor prerender data-fetching, per-call gRPC channel
  creation instead of reuse). Those become their own changes, each using the
  tracing added here to show before/after impact.

## Capabilities

### New Capabilities
- `observability`: cross-cutting distributed tracing and metrics
  instrumentation across all three services, exported to Grafana Cloud via
  OTLP, giving a single end-to-end trace per user-facing request.

### Modified Capabilities
(none — this only adds instrumentation, no existing spec-level behavior
changes)

## Impact

- **Affected projects**: all three — `NinjagoScanner.Web`,
  `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`. This
  capability is genuinely cross-cutting (it's the same OTel wiring pattern
  repeated in each service, plus the gRPC context-propagation link between
  them), so it is unprefixed per this repo's OpenSpec convention rather than
  living under one project's `<project>-<capability>` naming.
- **New dependencies**: `OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
  `OpenTelemetry.Instrumentation.AspNetCore`,
  `OpenTelemetry.Instrumentation.Http`,
  `OpenTelemetry.Instrumentation.GrpcNetClient` (Web only, for the client
  side) in each affected `.csproj`.
- **New external dependency**: Grafana Cloud (OTLP endpoint + API token per
  service), added as Fly secrets — no new Fly app/machine.
- **No behavior change** for end users; this is additive instrumentation
  only.
