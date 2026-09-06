## Context

See proposal.md - Why. All three services already call
`builder.Services.AddOpenTelemetry().ConfigureResource(...).WithTracing(...).WithMetrics(...)`
in `Program.cs`, each `WithTracing`/`WithMetrics` block ending in an
`AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf)`
that reads `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` from Fly
secrets (see archived `add-opentelemetry-observability/design.md`). Logging
today is whatever `WebApplication.CreateBuilder` wires up by default (console
provider), untouched by any of that.

`OpenTelemetry.Extensions.Hosting` (already referenced at 1.18.0 in all three
`.csproj` files) exposes a `WithLogging(...)` extension on the same
`IOpenTelemetryBuilder` returned by `AddOpenTelemetry()`, alongside the
existing `WithTracing`/`WithMetrics`. It registers an OpenTelemetry
`ILoggerProvider` under the hood (equivalent to calling
`builder.Logging.AddOpenTelemetry(...)` directly) and shares the resource
already set up via `.ConfigureResource(...)`, so no second `service.name`
needs to be configured.

## Goals / Non-Goals

**Goals:**
- Reuse the exact `AddOtlpExporter` pattern (protocol forced to
  `HttpProtobuf`, endpoint/headers from env vars) already used for traces
  and metrics, so logging follows the same, now-familiar shape.
- Trace/span IDs on exported logs come for free from the OTel logging
  bridge's default behavior (it reads the ambient `Activity` created by the
  tracing instrumentation already in place) — no extra wiring needed beyond
  enabling the exporter.
- Keep the console provider active so `flyctl logs` keeps working exactly as
  it does today; this is additive, not a replacement.

**Non-Goals:**
- Changing what gets logged or at what level anywhere in the three services
  — this only changes where existing `ILogger` output ends up.
- Structured/semantic logging conventions (e.g. consistent field naming
  across log call sites) — out of scope, a separate concern from export
  plumbing.
- Log-based alerting or Grafana log dashboards — follow-up once logs are
  actually flowing, same reasoning the original observability change used
  for traces/metrics dashboards.

## Decisions

### `WithLogging(...)` on the existing `AddOpenTelemetry()` chain, not a separate `builder.Logging.AddOpenTelemetry(...)` call
Both achieve the same runtime result, but `WithLogging` slots into the exact
fluent chain `WithTracing`/`WithMetrics` already use, inherits the
`ConfigureResource(...)` call already there, and keeps all three signals
configured in one place in `Program.cs`. A standalone
`builder.Logging.AddOpenTelemetry(...)` call would need its own
`SetResourceBuilder(...)` to get the same `service.name`, duplicating
information already declared once.

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "ninjago-scanner-web"))
    .WithTracing(tracing => tracing /* unchanged */)
    .WithMetrics(metrics => metrics /* unchanged */)
    .WithLogging(logging => logging
        .AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf));
```

### `IncludeFormattedMessage = true`, default scopes/attributes otherwise
`WithLogging` takes an optional second delegate for `OpenTelemetryLoggerOptions`.
Setting `IncludeFormattedMessage = true` exports the fully-formatted message
string (what you'd see in console output) rather than only the raw template
+ parameters, so log entries are readable in Grafana without reconstructing
the template. No other option is changed — `IncludeScopes`/`ParseStateValues`
defaults are fine for this app's current logging usage (no scope-heavy
logging patterns exist in the codebase today).

### No new NuGet package
`OpenTelemetry.Exporter.OpenTelemetryProtocol` (already referenced by all
three projects for the trace/metric exporters) also provides the logs OTLP
exporter — `WithLogging(...).AddOtlpExporter(...)` resolves from the same
package. Nothing new to add to any `.csproj`.

### Same Fly secrets, no new ones
Reuses `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` already set
per app. Grafana Cloud's OTLP gateway accepts logs on the same endpoint as
traces/metrics and routes to Loki internally based on the payload type, so
no separate endpoint/token is needed (consistent with how one Grafana Cloud
instance already serves Tempo and Mimir for this app).

## Risks / Trade-offs

- **[Risk]** Logs now leave the machine to a third-party cloud endpoint,
  which console-only logging never did — any log statement that happens to
  include sensitive data (credentials, tokens) would now be transmitted
  externally, not just written to a Fly-controlled stdout stream →
  **Mitigation**: spot-check existing `ILogger` call sites in all three
  services during implementation for anything sensitive (none are expected —
  PictureService already avoids logging AWS credentials — but worth
  confirming once, not per future log statement).
- **[Risk]** Doubling the OTLP export volume per service (logs in addition
  to traces/metrics) could push Grafana Cloud free-tier usage limits sooner
  → **Mitigation**: same reasoning the original observability change used
  for 100% trace sampling — this app's traffic is personal-scale; revisit
  only if usage becomes a real constraint.
- **[Risk]** If `WithLogging` is added but the console provider is
  inadvertently removed (e.g. by a future refactor calling
  `builder.Logging.ClearProviders()`), `flyctl logs` would go silent without
  anyone noticing until they needed it → **Mitigation**: none needed now;
  call out in tasks.md to explicitly verify console output still appears
  after the change, rather than only checking Grafana.

## Migration Plan

- Additive only, same shape as the original observability change: add one
  `.WithLogging(...)` block to each service's existing `AddOpenTelemetry()`
  chain in `Program.cs`. No Fly secrets, no `.csproj`, no data migration.
- Services can be updated and deployed independently and in any order —
  each service's logs start flowing to Grafana as soon as that service is
  redeployed; a partial rollout just means some services' logs aren't in
  Grafana yet, not broken.
- Rollback: remove the `.WithLogging(...)` block (or it becomes a no-op if
  the shared `OTEL_EXPORTER_OTLP_ENDPOINT` secret is ever unset, same as
  tracing/metrics today) — no state to unwind.
