using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<SidecarCache>();
builder.Services.AddScoped(provider => new PictureScannerGrpcService(
    provider.GetRequiredService<IConfiguration>(),
    provider.GetRequiredService<ILogger<PictureScannerGrpcService>>(),
    provider.GetRequiredService<SidecarCache>()));

var app = builder.Build();

app.MapGrpcService<PictureScannerGrpcService>();
app.MapGet("/", () => "This service exposes card photo scanning via gRPC. Use a gRPC client to call CardPictureService endpoints.");

app.Run();
