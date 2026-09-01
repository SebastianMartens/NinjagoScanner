namespace NinjagoScanner.Web;

internal static class WebConfig
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

    public static string ResolveAuthDatabasePath(IConfiguration configuration)
    {
        return configuration["Auth:DatabasePath"]
               ?? Environment.GetEnvironmentVariable("AUTH_DATABASE_PATH")
               ?? (Environment.OSVersion.Platform == PlatformID.Win32NT ? "Data/users.db" : "/data/users.db");
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
}
