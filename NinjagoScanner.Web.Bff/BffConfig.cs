namespace NinjagoScanner.Web.Bff;

internal static class BffConfig
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    public static string ResolveCatalogServiceAddress(IConfiguration configuration)
    {
        return configuration["CatalogService:Address"]
               ?? Environment.GetEnvironmentVariable("CATALOG_SERVICE_ADDRESS")
               ?? "http://localhost:5073";
    }

    public static string ResolvePictureServiceAddress(IConfiguration configuration)
    {
        return configuration["PictureService:Address"]
               ?? Environment.GetEnvironmentVariable("PICTURE_SERVICE_ADDRESS")
               ?? "http://localhost:5169";
    }

    public static long ResolveMaxUploadBytes(IConfiguration configuration)
    {
        const long defaultMaxUploadBytes = 15 * 1024 * 1024;

        var configuredValue = configuration["CardPhotos:MaxUploadBytes"]
                              ?? Environment.GetEnvironmentVariable("CARD_PHOTOS_MAX_UPLOAD_BYTES");

        return long.TryParse(configuredValue, out var parsedValue) && parsedValue > 0
            ? parsedValue
            : defaultMaxUploadBytes;
    }

    /// <summary>
    /// The S3 bucket holding card photos, keyed by generated photo ID. Must be the same bucket
    /// PictureService itself is configured against (see PictureService's ScannerConfig).
    /// </summary>
    public static string ResolvePhotosBucketName(IConfiguration configuration)
    {
        return configuration["Storage:PhotosBucketName"]
               ?? configuration["PHOTOS_BUCKET_NAME"]
               ?? throw new InvalidOperationException(
                   "Storage:PhotosBucketName (or PHOTOS_BUCKET_NAME) must be configured — the S3 bucket for card photos.");
    }
}
