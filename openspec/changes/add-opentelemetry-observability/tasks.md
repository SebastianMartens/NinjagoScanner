## 1. Grafana Cloud setup

- [x] 1.1 Create (or reuse) a Grafana Cloud stack and note its OTLP gateway
      endpoint URL and an API token (Basic Auth: instance ID as username,
      token as password) with write access to Tempo and Mimir
- [x] 1.2 Set `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_HEADERS`
      as Fly secrets on all three apps (`ninjago-scanner-web`,
      `ninjago-scanner-catalog-service`, `ninjago-scanner-picture-service`)
      via `flyctl secrets set` (hand the actual secret-setting command to
      the user to run rather than running it directly)
      (done 2026-08-25; PictureService's `OTEL_EXPORTER_OTLP_HEADERS` was
      initially mistyped — caught by comparing Fly secret digests across
      the three apps, since CatalogService/Web matched and PictureService
      didn't — and Web's secrets were also stuck in `Staged` status until a
      full `flyctl deploy` rolled them out; see design.md if a similar
      "secrets set but no data arrives" issue comes up again)

## 2. NinjagoScanner.CatalogService instrumentation

- [x] 2.1 Add `OpenTelemetry.Extensions.Hosting`,
      `OpenTelemetry.Exporter.OpenTelemetryProtocol`, and
      `OpenTelemetry.Instrumentation.AspNetCore` package references
- [x] 2.2 Wire `AddOpenTelemetry()` in `Program.cs`: set the
      `service.name` resource attribute (`ninjago-scanner-catalog-service`),
      add ASP.NET Core instrumentation for traces, add a runtime/ASP.NET
      Core meter for metrics, and configure the OTLP exporter for both from
      `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS`
- [x] 2.3 Verify locally: run the service, make a `ListSeries`/`GetSeries`
      call, confirm a trace and metrics appear in Grafana Cloud
      (verified against the deployed Fly app instead of locally — see 5.2)

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
- [x] 3.3 Verify locally: call `ListCards`, confirm the resulting trace
      shows spans for the underlying S3/DynamoDB calls distinguishable from
      gRPC handling time
      (verified against the deployed Fly app instead of locally — trace
      `6bd5ecf78895dce87cd8b2c07a175837` shows a `GetPhotoDownloadUrls` span
      standing out as the likely dominant cost in the `/review` trace)

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
- [x] 4.4 Verify locally: load `/review` with CatalogService and
      PictureService also running instrumented (task 2, 3), confirm a
      single trace in Grafana Cloud spans all three services for one page
      load
      (verified against the deployed Fly app instead of locally — see 5.2)

## 5. Deploy and confirm end-to-end

- [x] 5.1 Deploy all three services to Fly (`flyctl deploy` per app, or via
      the existing `deploy-*.yml` GitHub Actions workflows)
- [x] 5.2 Load a real page (e.g. `/review`) against the deployed app,
      confirm a complete cross-service trace appears in Grafana Cloud Tempo
      and corresponding metrics appear in Mimir
- [x] 5.3 Confirm no user-visible regression: pages still load and function
      normally with instrumentation active (spot-check `/`, `/collection`,
      `/gallery`, `/review`, `/upload`)
- [x] 5.4 Capture a baseline: record trace waterfalls for the currently-slow
      pages (at least `/review`) as the "before" reference the two planned
      follow-up perf-fix changes will compare against
      (captured 2026-08-25: trace `6bd5ecf78895dce87cd8b2c07a175837` in
      Grafana Cloud Tempo for `/review`, ~90-98s end-to-end, spanning
      `ninjago-scanner-web` → `ninjago-scanner-catalog-service`/
      `ninjago-scanner-picture-service`; a `GetPhotoDownloadUrls` span
      inside picture-service stands out as the likely dominant cost — this
      is the "before" reference for the two follow-up perf-fix changes)
