using NinjagoScanner.Web;
using NinjagoScanner.Web.Components;
using NinjagoScanner.Web.Services;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// CatalogService/PictureService are only reachable over plain HTTP on Fly's private network (no
// TLS between internal services — see infra/README.md). Grpc.Net.Client requires HTTP/2, and
// SocketsHttpHandler refuses cleartext HTTP/2 (h2c) unless this switch is set, so without it every
// GrpcChannel.ForAddress("http://...") call below throws.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var catalogServiceAddress = WebConfig.ResolveCatalogServiceAddress(builder.Configuration);
var pictureServiceAddress = WebConfig.ResolvePictureServiceAddress(builder.Configuration);
var maxUploadBytes = WebConfig.ResolveMaxUploadBytes(builder.Configuration);

builder.Services.AddSingleton(_ => new CatalogServiceClient(catalogServiceAddress));
builder.Services.AddSingleton(_ => new PictureServiceClient(pictureServiceAddress, catalogServiceAddress, maxUploadBytes));
builder.Services.AddSingleton(provider => new CollectionQueryService(
    provider.GetRequiredService<CatalogServiceClient>(),
    provider.GetRequiredService<PictureServiceClient>()));

// OTLP endpoint/headers (OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_HEADERS) are read
// automatically by AddOtlpExporter() from the standard OTel environment variables — see
// openspec/changes/add-opentelemetry-observability/design.md. gRPC client instrumentation hooks
// into SocketsHttpHandler/HttpClient diagnostics, so CatalogServiceClient/PictureServiceClient's
// per-call GrpcChannel.ForAddress(...) calls pick it up with no changes to those classes. The
// trace batch export interval is shortened because this app's Fly machine has
// min_machines_running = 0 (autostop) — a longer default delay risks losing the last batch when
// the machine is stopped shortly after handling a request. Protocol is forced to HttpProtobuf
// because the .NET SDK otherwise defaults to gRPC, which Grafana Cloud's OTLP gateway doesn't
// accept (export would fail silently — non-blocking by design).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "ninjago-scanner-web"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 2000;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("System.Runtime")
        .AddOtlpExporter(options => options.Protocol = OtlpExportProtocol.HttpProtobuf));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// No HTTPS redirect/HSTS here: Fly's edge proxy terminates TLS and forwards plain HTTP to this
// app (fly.toml sets force_https), the same way CatalogService/PictureService already run without
// TLS on Fly's private network — see infra/README.md.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
