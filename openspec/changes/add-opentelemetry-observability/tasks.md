## 1. Grafana Cloud setup

- [ ] 1.1 Create (or reuse) a Grafana Cloud stack and note its OTLP gateway
      endpoint URL and an API token (Basic Auth: instance ID as username,
      token as password) with write access to Tempo and Mimir
- [ ] 1.2 Set `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS`
      as Fly secrets on all three apps (`ninjago-scanner-web`,
      `ninjago-scanner-catalog-service`, `ninjago-scanner-picture-service`)
      via `flyctl secrets set` (hand the actual secret-setting command to
      the user to run rather than running it directly)

## 2. NinjagoScanner.CatalogService instrumentation

- [x] 2.1 Add `OpenTelemetry.Extensions.Hosting`,
      `OpenTelemetry.Exporter.OpenTelemetryProtocol`, and
      `OpenTelemetry.Instrumentation.AspNetCore` package references
- [x] 2.2 Wire `AddOpenTelemetry()` in `Program.cs`: set the
      `service.name` resource attribute (`ninjago-scanner-catalog-service`),
      add ASP.NET Core instrumentation for traces, add a runtime/ASP.NET
      Core meter for metrics, and configure the OTLP exporter for both from
      `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS`
- [ ] 2.3 Verify locally: run the service, make a `ListSeries`/`GetSeries`
      call, confirm a trace and metrics appear in Grafana Cloud

## 3. NinjagoScanner.PictureService instrumentation

- [x] 3.1 Add `OpenTelemetry.Extensions.Hosting`,
      `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
      `OpenTelemetry.Instrumentation.AspNetCore`, and
      `OpenTelemetry.Instrumentation.Http` package references
- [x] 3.2 Wire `AddOpenTelemetry()` in `Program.cs`: set `service.name`
      (`ninjago-scanner-picture-service`), add ASP.NET Core instrumentation
      and HttpClient instrumentation (covers the AWS SDK's S3/DynamoDB
      calls, which go through `HttpClient` under the hood) for traces, add
      metrics, configure the OTLP exporter
- [ ] 3.3 Verify locally: call `ListCards`, confirm the resulting trace
      shows spans for the underlying S3/DynamoDB calls distinguishable from
      gRPC handling time

## 4. NinjagoScanner.Web instrumentation

- [x] 4.1 Add `OpenTelemetry.Extensions.Hosting`,
      `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
      `OpenTelemetry.Instrumentation.AspNetCore`, and
      `OpenTelemetry.Instrumentation.GrpcNetClient` package references
- [x] 4.2 Wire `AddOpenTelemetry()` in `Program.cs`: set `service.name`
      (`ninjago-scanner-web`), add ASP.NET Core instrumentation (inbound
      Blazor/SignalR requests) and gRPC client instrumentation (outbound
      calls) for traces, add metrics, configure the OTLP exporter
- [x] 4.3 Confirm `CatalogServiceClient` and `PictureServiceClient`'s
      per-call `GrpcChannel.ForAddress(...)` calls pick up the gRPC client
      instrumentation automatically (it hooks in via `SocketsHttpHandler`/
      `HttpClient` diagnostics, not per-channel config) — no code change to
      those classes should be needed for spans to appear
- [ ] 4.4 Verify locally: load `/review` with CatalogService and
      PictureService also running instrumented (task 2, 3), confirm a
      single trace in Grafana Cloud spans all three services for one page
      load

## 5. Deploy and confirm end-to-end

- [ ] 5.1 Deploy all three services to Fly (`flyctl deploy` per app, or via
      the existing `deploy-*.yml` GitHub Actions workflows)
- [ ] 5.2 Load a real page (e.g. `/review`) against the deployed app,
      confirm a complete cross-service trace appears in Grafana Cloud Tempo
      and corresponding metrics appear in Mimir
- [ ] 5.3 Confirm no user-visible regression: pages still load and function
      normally with instrumentation active (spot-check `/`, `/collection`,
      `/gallery`, `/review`, `/upload`)
- [ ] 5.4 Capture a baseline: record trace waterfalls for the currently-slow
      pages (at least `/review`) as the "before" reference the two planned
      follow-up perf-fix changes will compare against
