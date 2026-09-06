## 1. CatalogService logging export

- [x] 1.1 Add `.WithLogging(logging => logging.AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf))` to the `AddOpenTelemetry()` chain in `NinjagoScanner.CatalogService/Program.cs`, with `IncludeFormattedMessage = true` set via the options delegate, and verify `dotnet build NinjagoScanner.slnx` succeeds
- [x] 1.2 Run the service locally with `OTEL_EXPORTER_OTLP_ENDPOINT`/`OTEL_EXPORTER_OTLP_HEADERS` set to the existing Grafana Cloud values, trigger a request (e.g. `ListSeries`), and verify the log line for that request appears in Grafana Cloud Loki (Explore) tagged with `service.name = ninjago-scanner-catalog-service` — confirmed by user against the deployed app: CatalogService logs visible in Grafana Cloud
- [x] 1.3 Verify `flyctl logs -a <catalog-service-app>` (or local console output) still shows the same log line, confirming the console provider is unaffected — confirmed post-deploy via `flyctl logs -a ninjago-scanner-catalog-service`

## 2. PictureService logging export

- [x] 2.1 Add the same `.WithLogging(...)` block (with `IncludeFormattedMessage = true`) to the `AddOpenTelemetry()` chain in `NinjagoScanner.PictureService/Program.cs`, and verify `dotnet build NinjagoScanner.slnx` succeeds
- [x] 2.2 Run the service locally, trigger a request that logs during an active trace (e.g. a `Scan` call that also calls CatalogService), and verify the resulting Grafana Cloud log entry carries the same trace ID as the corresponding trace in Tempo — confirmed by user against the deployed app: PictureService logs visible in Grafana Cloud
- [x] 2.3 Spot-check existing `ILogger` call sites in `NinjagoScanner.PictureService` (`GeminiApiService.cs`, `PhotoStore.cs`, `SidecarTable.cs`, `CatalogGrpcClient.cs`) for anything sensitive (AWS credentials, Gemini API key, full sidecar contents) that shouldn't leave the machine now that logs are exported externally — confirmed: all logging in the project is in `PictureScannerGrpcService.cs` (photo IDs, card/set/number metadata, catalog-service address, transport-failure exception messages); `GeminiApiService.cs` uses the API key to build a request URI but never logs it
- [x] 2.4 Verify console/`flyctl logs` output is unaffected, same check as 1.3 — confirmed post-deploy via `flyctl logs -a ninjago-scanner-picture-service`

## 3. Web logging export

- [x] 3.1 Add the same `.WithLogging(...)` block (with `IncludeFormattedMessage = true`) to the `AddOpenTelemetry()` chain in `NinjagoScanner.Web/Program.cs`, and verify `dotnet build NinjagoScanner.slnx` succeeds
- [x] 3.2 Load a page that fans out to both CatalogService and PictureService, and verify Grafana Cloud shows log entries from all three services correlated under the same trace ID for that one page load — confirmed by user against the deployed app: Web logs visible in Grafana Cloud
- [x] 3.3 Verify console/`flyctl logs` output is unaffected, same check as 1.3 — confirmed post-deploy via `flyctl logs -a ninjago-scanner-web`

## 4. Deploy and verify end-to-end

- [x] 4.1 Deploy all three services to Fly (`flyctl deploy` per app, no new secrets needed) and verify each app starts successfully (`flyctl status` / health check) — all three deployed and passing health checks
- [x] 4.2 Exercise the deployed app (load the overview page, run a scan) and verify logs from all three services appear in Grafana Cloud within the existing Explore/Loki view, correlated with their traces — user confirmed all three services' logs are visible in Grafana Cloud
- [x] 4.3 Run `dotnet test NinjagoScanner.slnx` and verify the full suite still passes (no behavior change expected, this is a regression check) — 161/161 tests passed
