using Microsoft.Extensions.Configuration;
using NinjagoScanner.Scanner.Abstractions;

namespace NinjagoScanner.Scanner;

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
    public required string CardPhotosDirectory { get; init; }
    public required string SeriesCatalogPath { get; init; }
    public required bool OverwriteExistingSidecars { get; init; }
    public required int DelayBetweenRequestsMs { get; init; }
    public required int RetryDelayMs { get; init; }
    public required int MaxAttempts { get; init; }
    public required int TimeoutSeconds { get; init; }

    public static ScannerConfig Load(IConfiguration configuration, GeminiScanRequest? request)
    {
        return new ScannerConfig
        {
            ApiKey = request?.ApiKey ?? configuration["Gemini:ApiKey"] ?? configuration["GEMINI_API_KEY"] ?? string.Empty,
            Model = request?.Model ?? configuration["Gemini:Model"] ?? configuration["GEMINI_MODEL"] ?? "gemini-3.1-flash-lite",
            CardPhotosDirectory = request?.CardPhotosDirectory ?? configuration["CardPhotos:Directory"] ?? configuration["CARD_PHOTOS_DIRECTORY"] ?? ResolveDefaultCardPhotosDirectory(),
            SeriesCatalogPath = request?.SeriesCatalogPath ?? configuration["CardSeries:Path"] ?? configuration["CARD_SERIES_PATH"] ?? ResolveDefaultSeriesCatalogPath(),
            OverwriteExistingSidecars = request?.OverwriteExistingSidecars ?? (bool.TryParse(configuration["Scanner:OverwriteSidecars"] ?? configuration["OVERWRITE_SIDECARS"], out var overwrite) && overwrite),
            DelayBetweenRequestsMs = request?.DelayBetweenRequestsMs ?? TryParseInt(configuration["Scanner:DelayBetweenRequestsMs"] ?? configuration["DELAY_BETWEEN_REQUESTS_MS"], 1000),
            RetryDelayMs = request?.RetryDelayMs ?? TryParseInt(configuration["Scanner:RetryDelayMs"] ?? configuration["RETRY_DELAY_MS"], 3000),
            MaxAttempts = Math.Max(1, request?.MaxAttempts ?? TryParseInt(configuration["Scanner:MaxAttempts"] ?? configuration["MAX_ATTEMPTS"], 3)),
            TimeoutSeconds = Math.Max(10, request?.TimeoutSeconds ?? TryParseInt(configuration["Scanner:HttpTimeoutSeconds"] ?? configuration["HTTP_TIMEOUT_SECONDS"], 90))
        };
    }

    private static int TryParseInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsedValue) ? parsedValue : fallback;
    }

    private static string ResolveDefaultCardPhotosDirectory()
    {
        var candidateDirectories = GetDefaultCardPhotosCandidates();

        foreach (var candidate in candidateDirectories)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidateDirectories[0];
    }

    private static string ResolveDefaultSeriesCatalogPath()
    {
        var candidatePaths = GetDefaultSeriesCatalogCandidates();

        foreach (var candidate in candidatePaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidatePaths[0];
    }

    private static IReadOnlyList<string> GetDefaultCardPhotosCandidates()
    {
        return
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardFotos")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardFotos")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardFotos")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardFotos"))
        ];
    }

    private static IReadOnlyList<string> GetDefaultSeriesCatalogCandidates()
    {
        return
        [
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardInfos", "series.json")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardInfos", "series.json")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardInfos", "series.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardInfos", "series.json"))
        ];
    }
}
