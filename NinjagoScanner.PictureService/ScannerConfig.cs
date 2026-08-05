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
    public required string CardPhotosDirectory { get; init; }
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
            CardPhotosDirectory = (request?.HasCardPhotosDirectory ?? false ? request.CardPhotosDirectory : null) ?? configuration["CardPhotos:Directory"] ?? configuration["CARD_PHOTOS_DIRECTORY"] ?? ResolveDefaultCardPhotosDirectory(),
            CatalogServiceAddress = (request?.HasCatalogServiceAddress ?? false ? request.CatalogServiceAddress : null) ?? configuration["CatalogService:Address"] ?? configuration["CATALOG_SERVICE_ADDRESS"] ?? "http://localhost:5073",
            OverwriteExistingSidecars = request?.HasOverwriteExistingSidecars ?? false ? request.OverwriteExistingSidecars : (bool.TryParse(configuration["Scanner:OverwriteSidecars"] ?? configuration["OVERWRITE_SIDECARS"], out var overwrite) && overwrite),
            DelayBetweenRequestsMs = request?.HasDelayBetweenRequestsMs ?? false ? request.DelayBetweenRequestsMs : TryParseInt(configuration["Scanner:DelayBetweenRequestsMs"] ?? configuration["DELAY_BETWEEN_REQUESTS_MS"], 1000),
            RetryDelayMs = request?.HasRetryDelayMs ?? false ? request.RetryDelayMs : TryParseInt(configuration["Scanner:RetryDelayMs"] ?? configuration["RETRY_DELAY_MS"], 3000),
            MaxAttempts = Math.Max(1, request?.HasMaxAttempts ?? false ? request.MaxAttempts : TryParseInt(configuration["Scanner:MaxAttempts"] ?? configuration["MAX_ATTEMPTS"], 3)),
            TimeoutSeconds = Math.Max(10, request?.HasTimeoutSeconds ?? false ? request.TimeoutSeconds : TryParseInt(configuration["Scanner:HttpTimeoutSeconds"] ?? configuration["HTTP_TIMEOUT_SECONDS"], 90))
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

    private static IReadOnlyList<string> GetDefaultCardPhotosCandidates()
    {
        var candidates = new List<string>
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardFotos")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardFotos")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardFotos")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardFotos"))
        };

        var gitMainRepoRoot = TryGetGitMainRepoRoot(Environment.CurrentDirectory)
                              ?? TryGetGitMainRepoRoot(AppContext.BaseDirectory);
        if (gitMainRepoRoot is not null)
        {
            candidates.Insert(0, Path.GetFullPath(Path.Combine(gitMainRepoRoot, "cardFotos")));
        }

        return candidates;
    }

    private static string? TryGetGitMainRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var gitFile = Path.Combine(dir.FullName, ".git");
            if (File.Exists(gitFile))
            {
                var content = File.ReadAllText(gitFile).Trim();
                if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    var gitdirPath = content["gitdir:".Length..].Trim();
                    // Path format: <main_repo>/.git/worktrees/<branch>
                    var worktreesDir = new DirectoryInfo(gitdirPath)?.Parent?.Parent;
                    if (worktreesDir?.Parent is { } mainRepoRoot)
                    {
                        return mainRepoRoot.FullName;
                    }
                }
            }
            dir = dir.Parent;
        }
        return null;
    }

}
