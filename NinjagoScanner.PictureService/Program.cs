using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

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

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonDynamoDB>();

builder.Services.AddSingleton<IPhotoStore>(provider => new PhotoStore(
    provider.GetRequiredService<IAmazonS3>(),
    ScannerConfig.ResolvePhotosBucketName(provider.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<ISidecarStore>(provider => new SidecarTable(
    provider.GetRequiredService<IAmazonDynamoDB>(),
    ScannerConfig.ResolveSidecarTableName(provider.GetRequiredService<IConfiguration>())));
builder.Services.AddSingleton<SidecarCache>();

builder.Services.AddScoped(provider => new PictureScannerGrpcService(
    provider.GetRequiredService<IConfiguration>(),
    provider.GetRequiredService<ILogger<PictureScannerGrpcService>>(),
    provider.GetRequiredService<SidecarCache>(),
    provider.GetRequiredService<IPhotoStore>()));

var app = builder.Build();

app.MapGrpcService<PictureScannerGrpcService>();
app.MapGet("/", () => "This service exposes card photo scanning via gRPC. Use a gRPC client to call CardPictureService endpoints.");

app.Run();
