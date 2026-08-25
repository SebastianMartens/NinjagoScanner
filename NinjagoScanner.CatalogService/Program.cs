using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddSingleton<CatalogRepository>();

// OTLP endpoint/headers (OTEL_EXPORTER_OTLP_ENDPOINT / OTEL_EXPORTER_OTLP_HEADERS) are read
// automatically by AddOtlpExporter() from the standard OTel environment variables — see
// openspec/changes/add-opentelemetry-observability/design.md.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "ninjago-scanner-catalog-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("System.Runtime")
        .AddOtlpExporter());

builder.WebHost.ConfigureKestrel(options =>
{
    // gRPC requires HTTP/2, but without TLS Kestrel can't use ALPN to negotiate
    // per-connection — an endpoint left at the default Http1AndHttp2 falls back to
    // HTTP/1.1 for every connection, so gRPC calls fail with HTTP_1_1_REQUIRED.
    options.ConfigureEndpointDefaults(listenOptions => listenOptions.Protocols = HttpProtocols.Http2);

    // Optional separate HTTP/1.1-only port for a plain GET "/" liveness probe (the
    // AWS internal NLB's health check — see infra/modules/internal-lb) that can't
    // share the gRPC port for the same ALPN-less reason.
    var healthCheckPort = builder.Configuration.GetValue<int?>("Kestrel:HealthCheckPort");
    if (healthCheckPort is int port)
    {
        // Any explicit Listen call here makes Kestrel abandon its implicit
        // ASPNETCORE_HTTP_PORTS-based endpoint entirely (it logs "Overriding
        // address(es) ... Binding to endpoints defined via IConfiguration and/or
        // UseKestrel() instead"), silently dropping the gRPC port — so once the
        // health-check port is configured, the main port needs its own explicit
        // Listen call too.
        var mainPort = builder.Configuration.GetValue<int?>("ASPNETCORE_HTTP_PORTS") ?? 8080;
        options.ListenAnyIP(mainPort, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        options.ListenAnyIP(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<CardCatalogGrpcService>();
app.MapGet("/", () => "This service exposes card catalog data via gRPC. Use a gRPC client to call CardCatalog endpoints.");

app.Run();
