## Context

See proposal.md - Why. All three services currently log only through
default `ILogger` to console (Fly captures stdout as plain text logs); there
is no tracing, no metrics, and no `OpenTelemetry` package anywhere in the
solution. Two follow-up perf-fix changes (removing duplicate Blazor
prerender data-fetching, and gRPC channel reuse in `NinjagoScanner.Web`)
depend on this change landing first so they have real trace data to show
before/after impact.

Deployment shape (from `fly.toml` in each project): three independently
deployed Fly apps in one org, `NinjagoScanner.Web` public, CatalogService
and PictureService reachable only over Fly's private network (6PN). All
gRPC traffic between services is plaintext HTTP/2 (h2c) — no TLS internally.
`NinjagoScanner.Web`'s Fly app has `min_machines_running = 0` with
autostart/autostop, so its machine can fully cold-start.

## Goals / Non-Goals

**Goals:**
- One trace per user-facing request, correlated across all services that
  request touches, viewable as a single waterfall in Grafana Cloud Tempo.
- Minimal added operational surface: no new Fly app, no self-hosted
  collector — export straight to Grafana Cloud's OTLP endpoint.
- Instrumentation that survives the two planned follow-up changes without
  rework (i.e. don't hand-instrument around the specific bugs being fixed;
  instrument the general request path so those bugs become visible in the
  data, and their fixes show up as the data changing).

**Non-Goals:**
- Fixing the perf issues themselves (duplicate prerender fetch, per-call
  gRPC channel creation) — separate changes.
- Log aggregation/shipping (Loki) — Fly's existing log capture is
  sufficient for now; traces/metrics are the gap being closed.
- Alerting/dashboards — configuring Grafana Cloud alert rules or building
  dashboards is follow-up work once real data exists, not part of this
  change.
- Sampling strategy tuning for high-traffic production use — this app's
  traffic volume is low (personal-scale), so this change uses
  always-on/100% sampling; revisit only if volume or cost ever makes that
  a problem.

## Decisions

### Direct OTLP export to Grafana Cloud, no local collector
Grafana Cloud accepts OTLP directly (Tempo for traces, Mimir for metrics).
Running a local Grafana Alloy/OTel Collector process would add a fourth
piece of infrastructure per environment for buffering/retry benefits this
app's traffic volume doesn't need. Each service's `OpenTelemetryProtocol`
exporter talks straight to Grafana Cloud's OTLP endpoint, authenticated via
Basic Auth (instance ID + API token) passed as `OTEL_EXPORTER_OTLP_HEADERS`.
Alternative considered: self-hosted Jaeger/Prometheus/Grafana stack on
another Fly machine — rejected as contrary to the "simpler/cheaper hosting"
bias that drove the Fly migration itself (see archived
`fly-hosting-migration` design.md).

### Auto-instrumentation over hand-written spans, for the request path already suspected
Use `OpenTelemetry.Instrumentation.AspNetCore` (inbound gRPC/HTTP),
`OpenTelemetry.Instrumentation.Http` (outbound HttpClient — covers AWS SDK
calls in PictureService), and `OpenTelemetry.Instrumentation.GrpcNetClient`
(outbound gRPC calls from Web's `CatalogServiceClient`/
`PictureServiceClient`) rather than hand-instrumenting each call site.
These three auto-instrumentation packages cover every hop currently
suspected of contributing to page slowness (Blazor's inbound request, the
outbound gRPC calls between services, and PictureService's outbound AWS
calls) without needing to modify application code to add spans manually.
Custom `ActivitySource` spans are left as a follow-up if a specific hop
later needs finer-grained breakdown (e.g. inside `SidecarCache` to
distinguish a cache hit from a DynamoDB read) — not needed for the initial
goal of seeing which hop dominates a slow request.

### W3C trace context propagation over gRPC metadata
`OpenTelemetry.Instrumentation.GrpcNetClient` on the Web side and
`OpenTelemetry.Instrumentation.AspNetCore` on the receiving services both
default to W3C Trace Context propagation, which `Grpc.Net.Client`/
`Grpc.AspNetCore.Server` carry automatically via gRPC metadata headers — no
custom propagator or manual header plumbing needed. This is what makes a
single Web page load show up as one trace spanning into CatalogService and
PictureService rather than three disconnected traces.

### Secrets via Fly secrets, one OTLP endpoint/token pair per service
`OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS` set via
`flyctl secrets set` per app, matching how AWS credentials are already
handled for PictureService (see `infra/README.md`). Using the same
Grafana Cloud instance/token for all three services is fine — Grafana Cloud
distinguishes services by the OTel `service.name` resource attribute, not
by which token was used.

### 100% sampling
At this app's traffic volume (personal-scale collection tracker), always-on
sampling is simplest and guarantees the two follow-up changes get complete
before/after data rather than a statistical sample that might miss the
specific slow requests being investigated. Revisit if Grafana Cloud's free
tier volume becomes a constraint.

## Risks / Trade-offs

- **[Risk]** Plaintext internal gRPC (h2c, no TLS) means trace context
  headers travel unencrypted between services → **Mitigation**: this is
  already true of all gRPC traffic on Fly's private 6PN network today
  (see Program.cs comment on `Http2UnencryptedSupport`); trace context adds
  no new exposure beyond what already exists.
- **[Risk]** `min_machines_running = 0` on `NinjagoScanner.Web` means a
  cold-started machine's first exported batch could be delayed or lost if
  the process is killed before flushing → **Mitigation**: configure the
  OTLP exporter's batch processor with a short export interval, and ensure
  `WebApplication` shutdown flushes the tracer/meter provider on `SIGTERM`
  (`AddOpenTelemetry()`'s default `TracerProvider`/`MeterProvider` disposal
  via DI container shutdown handles this).
- **[Risk]** 100% sampling could hit Grafana Cloud free tier limits if
  usage grows → **Mitigation**: not a concern at current traffic; revisit
  sampling rate if/when it becomes one.

## Migration Plan

- Additive only — no data migration, no breaking change. Each service adds
  the OTel packages, wires `AddOpenTelemetry()` in its `Program.cs`, and
  gets Fly secrets set before deploy. Services can be instrumented and
  deployed one at a time (e.g. PictureService first, since it's furthest
  from the browser and has the AWS calls) — partial rollout just means
  traces are incomplete (missing spans from not-yet-instrumented services)
  until all three are deployed, not broken.
- Rollback: remove/disable the OTel wiring (or unset the Fly secrets, which
  makes the exporter a no-op if endpoint is empty) — no state to unwind.
