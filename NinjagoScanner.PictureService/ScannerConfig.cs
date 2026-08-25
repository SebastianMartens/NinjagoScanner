using Microsoft.Extensions.Configuration;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.PictureService;

internal sealed class ScannerConfig
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    public required string ApiKey { get; init; }
    public required string Model { get; init; }
    public required string CatalogServiceAddress { get; init; }
    public required bool OverwriteExistingSidecars { get; init; }
    public required int DelayBetweenRequestsMs { get; init; }
    public required int RetryDelayMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required int TimeoutSeconds { get; init; }

    public static ScannerConfig Load(IConfiguration configuration, ScanRequest? request)
    {
        return new ScannerConfig
        {
            ApiKey = (request?.HasApiKey ?? false ? request.ApiKey : null) ?? configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"] ?? string.Empty,
            Model = (request?.HasModel ?? false ? request.Model : null) ?? configuration["Gemini:Model"] ?? configuration["GEMINI_MODEL"] ?? "gemini-3.1-flash-lite",
            CatalogServiceAddress = (request?.HasCatalogServiceAddress ?? false ? request.CatalogServiceAddress : null) ?? configuration["CatalogService:Address"] ?? configuration["CATALOG_SERVICE_ADDRESS"] ?? "http://localhost:5073",
            OverwriteExistingSidecars = request?.HasOverwriteExistingSidecars ?? false ? request.OverwriteExistingSidecars : (bool.TryParse(configuration["Scanner:OverwriteSidecars"] ?? configuration["OVERWRITE_SIDECARS"], out var overwrite) && overwrite),
            DelayBetweenRequestsMs = request?.HasDelayBetweenRequestsMs ?? false ? request.DelayBetweenRequestsMs : TryParseInt(configuration["Scanner:DelayBetweenRequestsMs"] ?? configuration["DELAY_BETWEEN_REQUESTS_MS"], 1000),
            RetryDelayMs = request?.HasRetryDelayMs ?? false ? request.RetryDelayMs : TryParseInt(configuration["Scanner:RetryDelayMs"] ?? configuration["RETRY_DELAY_MS"], 3000),
            MaxAttempts = Math.Max(1, request?.HasMaxAttempts ?? false ? request.MaxAttempts : TryParseInt(configuration["Scanner:MaxAttempts"] ?? configuration["MAX_ATTEMPTS"], 3)),
            TimeoutSeconds = Math.Max(10, request?.HasTimeoutSeconds ?? false ? request.TimeoutSeconds : TryParseInt(configuration["Scanner:HttpTimeoutSeconds"] ?? configuration["HTTP_TIMEOUT_SECONDS"], 90))
        };
    }

    public static ScannerConfig Load(IConfiguration configuration, UploadPhotoMetadata metadata)
    {
        return new ScannerConfig
        {
            ApiKey = (metadata.HasApiKey ? metadata.ApiKey : null) ?? configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"] ?? string.Empty,
            Model = (metadata.HasModel ? metadata.Model : null) ?? configuration["Gemini:Model"] ?? configuration["GEMINI_MODEL"] ?? "gemini-3.1-flash-lite",
            CatalogServiceAddress = (metadata.HasCatalogServiceAddress ? metadata.CatalogServiceAddress : null) ?? configuration["CatalogService:Address"] ?? configuration["CATALOG_SERVICE_ADDRESS"] ?? "http://localhost:5073",
            OverwriteExistingSidecars = true,
            DelayBetweenRequestsMs = 0,
            RetryDelayMs = metadata.HasRetryDelayMs ? metadata.RetryDelayMs : TryParseInt(configuration["Scanner:RetryDelayMs"] ?? configuration["RETRY_DELAY_MS"], 3000),
            MaxAttempts = Math.Max(1, metadata.HasMaxAttempts ? metadata.MaxAttempts : TryParseInt(configuration["Scanner:MaxAttempts"] ?? configuration["MAX_ATTEMPTS"], 3)),
            TimeoutSeconds = Math.Max(10, metadata.HasTimeoutSeconds ? metadata.TimeoutSeconds : TryParseInt(configuration["Scanner:HttpTimeoutSeconds"] ?? configuration["HTTP_TIMEOUT_SECONDS"], 90))
        };
    }

    private static int TryParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsedValue) ? parsedValue : fallback;
    }

    /// <summary>
    /// The S3 bucket holding card photos, keyed by generated photo ID (see <see cref="PhotoStore"/>).
    /// </summary>
    public static string ResolvePhotosBucketName(IConfiguration configuration)
    {
        return configuration["Storage:PhotosBucketName"]
               ?? configuration["PHOTOS_BUCKET_NAME"]
               ?? throw new InvalidOperationException(
                   "Storage:PhotosBucketName (or PHOTOS_BUCKET_NAME) must be configured — the S3 bucket for card photos.");
    }

    /// <summary>
    /// The DynamoDB table holding sidecar records, keyed by generated photo ID (see <see cref="SidecarTable"/>).
    /// </summary>
    public static string ResolveSidecarTableName(IConfiguration configuration)
    {
        return configuration["Storage:SidecarTableName"]
               ?? configuration["SIDECAR_TABLE_NAME"]
               ?? throw new InvalidOperationException(
                   "Storage:SidecarTableName (or SIDECAR_TABLE_NAME) must be configured — the DynamoDB table for sidecar records.");
    }
}
