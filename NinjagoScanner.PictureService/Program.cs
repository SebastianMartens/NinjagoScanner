using NinjagoScanner.PictureService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<PictureScannerGrpcService>();
app.MapGet("/", () => "This service exposes card photo scanning via gRPC. Use a gRPC client to call CardPictureService endpoints.");

app.Run();
