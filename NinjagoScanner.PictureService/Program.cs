using Amazon.DynamoDBv2;
using Amazon.S3;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

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
