using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();
builder.Services.AddSingleton<CatalogRepository>();

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
        options.ListenAnyIP(port, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<CardCatalogGrpcService>();
app.MapGet("/", () => "This service exposes card catalog data via gRPC. Use a gRPC client to call CardCatalog endpoints.");

app.Run();
