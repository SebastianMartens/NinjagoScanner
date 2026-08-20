using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.S3;
using Grpc.Core;
using NinjagoScanner.Web.Bff;
using NinjagoScanner.Web.Bff.Services;
using NinjagoScanner.Web.Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// CatalogService/PictureService are only reachable over plain HTTP inside the VPC (the internal
// NLB in front of them terminates no TLS - see infra/modules/bff-lambda/main.tf). Grpc.Net.Client
// requires HTTP/2, and SocketsHttpHandler refuses cleartext HTTP/2 (h2c) unless this switch is
// set, so without it every GrpcChannel.ForAddress("http://...") call below throws.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

// No-op outside a Lambda runtime, so this stays safe for local `dotnet run` and Fargate-style
// hosting too. Wires this app up to run behind API Gateway (HTTP API) once someone deploys it
// as a Lambda function (see openspec task 9.1) without any further code changes here.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var catalogServiceAddress = BffConfig.ResolveCatalogServiceAddress(builder.Configuration);
var pictureServiceAddress = BffConfig.ResolvePictureServiceAddress(builder.Configuration);
var maxUploadBytes = BffConfig.ResolveMaxUploadBytes(builder.Configuration);

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddSingleton<IUploadUrlIssuer>(provider => new S3UploadUrlIssuer(
    provider.GetRequiredService<IAmazonS3>(),
    BffConfig.ResolvePhotosBucketName(provider.GetRequiredService<IConfiguration>())));

builder.Services.AddSingleton(provider => new CardCatalogService(
    catalogServiceAddress,
    pictureServiceAddress,
    provider.GetRequiredService<IUploadUrlIssuer>(),
    maxUploadBytes));
builder.Services.AddSingleton(new PictureServiceClient(pictureServiceAddress));

var allowedClientOrigin = builder.Configuration["Cors:ClientOrigin"] ?? Environment.GetEnvironmentVariable("WEB_CLIENT_ORIGIN");
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (!string.IsNullOrWhiteSpace(allowedClientOrigin))
        {
            policy.WithOrigins(allowedClientOrigin).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            // No explicit origin configured (typical for local dev, where the WASM dev server
            // runs on a different port): allow any origin rather than block local development.
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

app.UseCors();

var api = app.MapGroup("/api");

api.MapGet("/series", async (CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.GetKnownSeriesAsync(cancellationToken)));

api.MapGet("/collection/overview", async (CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.GetCollectionOverviewAsync(cancellationToken)));

api.MapGet("/collection/details", async (string series, string cardNumber, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    var details = await catalogService.GetCollectionCardDetailsAsync(series, cardNumber, cancellationToken);
    return details is null ? Results.NotFound() : Results.Ok(details);
});

api.MapGet("/gallery", async (string series, CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.GetGalleryCardsAsync(series, cancellationToken)));

api.MapGet("/series-summary", async (CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.GetSeriesSummaryAsync(cancellationToken)));

api.MapGet("/review-groups", async (CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.GetReviewGroupsAsync(cancellationToken)));

api.MapPut("/photos/{photoId}/sidecar", async (string photoId, CollectionCardSidecarUpdate update, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    await catalogService.UpdateCardSidecarAsync(photoId, update, cancellationToken);
    return Results.NoContent();
});

api.MapPut("/photos/{photoId}/review-status", async (string photoId, UpdateReviewStatusRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    await catalogService.UpdateReviewStatusAsync(photoId, request.ReviewStatus, cancellationToken);
    return Results.NoContent();
});

api.MapPut("/photos/{photoId}/set-name", async (string photoId, UpdateSetNameRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    await catalogService.UpdateSetNameAsync(photoId, request.SetName, cancellationToken);
    return Results.NoContent();
});

api.MapPut("/photos/{photoId}/card-number", async (string photoId, UpdateCardNumberRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    await catalogService.UpdateCardNumberAsync(photoId, request.CardNumber, cancellationToken);
    return Results.NoContent();
});

api.MapPut("/photos/{photoId}/language", async (string photoId, UpdateCardLanguageRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    await catalogService.UpdateCardLanguageAsync(photoId, request.Language, cancellationToken);
    return Results.NoContent();
});

api.MapDelete("/photos/{photoId}", async (string photoId, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    try
    {
        await catalogService.DeletePhotoAsync(photoId, cancellationToken);
        return Results.NoContent();
    }
    catch (RpcException exception) when (exception.StatusCode == StatusCode.NotFound)
    {
        return Results.NotFound();
    }
});

api.MapPost("/scan", async (PictureServiceClient pictureServiceClient, CancellationToken cancellationToken) =>
    Results.Ok(await pictureServiceClient.ScanAsync(catalogServiceAddress, cancellationToken)));

api.MapGet("/uploads/limits", (CardCatalogService catalogService) =>
    Results.Ok(new UploadLimitsDto { MaxUploadBytes = catalogService.MaxUploadBytes }));

api.MapPost("/uploads", async (UploadUrlRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
{
    try
    {
        var (photoId, contentType) = catalogService.ValidateUpload(request.FileName, request.FileSizeBytes, request.ContentType);
        var uploadUrl = await catalogService.CreateUploadUrlAsync(photoId, contentType, cancellationToken);
        return Results.Ok(new UploadUrlResponseDto { PhotoId = photoId, UploadUrl = uploadUrl });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

api.MapPost("/uploads/{photoId}/confirm", async (string photoId, ConfirmUploadRequestDto request, CardCatalogService catalogService, CancellationToken cancellationToken) =>
    Results.Ok(await catalogService.ConfirmUploadAsync(photoId, request.SourceFileName, cancellationToken)));

app.Run();

public partial class Program;
