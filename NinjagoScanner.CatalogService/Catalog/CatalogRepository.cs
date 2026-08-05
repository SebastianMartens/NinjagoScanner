using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinjagoScanner.CatalogService.Catalog;

public sealed partial class CatalogRepository(ILogger<CatalogRepository> logger, IWebHostEnvironment environment, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object gate = new();
    private CatalogSnapshot? cachedSnapshot;
    private long cachedStamp;

    private readonly string dataDirectoryPath = ResolveDataDirectory(environment, configuration);
    private readonly string mainCatalogPath = Path.Combine(ResolveDataDirectory(environment, configuration), "series.json");

    public CatalogSnapshot GetSnapshot()
    {
        lock (gate)
        {
            var currentStamp = ComputeCatalogStamp(dataDirectoryPath);
            if (cachedSnapshot is not null && currentStamp == cachedStamp)
            {
                return cachedSnapshot;
            }

            cachedSnapshot = LoadSnapshot();
            cachedStamp = currentStamp;
            return cachedSnapshot;
        }
    }

    public SeriesCatalogItem? FindByName(string seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return null;
        }

        var requestedKey = NormalizeLookupKey(seriesName);
        return GetSnapshot().Series.FirstOrDefault(entry => NormalizeLookupKey(entry.SeriesName) == requestedKey);
    }

    private CatalogSnapshot LoadSnapshot()
    {
        if (!File.Exists(mainCatalogPath))
        {
            logger.LogWarning("Catalog file not found at {CatalogPath}", mainCatalogPath);
            return new CatalogSnapshot
            {
                DataDirectory = dataDirectoryPath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                Series = Array.Empty<SeriesCatalogItem>()
            };
        }

        try
        {
            var rootJson = File.ReadAllText(mainCatalogPath, Encoding.UTF8);
            var root = JsonSerializer.Deserialize<SeriesCatalogRoot>(rootJson, JsonOptions);
            var cardNamesBySeries = LoadSeriesCardNames(dataDirectoryPath);

            var series = root?.Series?
                .Where(item => !string.IsNullOrWhiteSpace(item.Serie))
                .Select(item =>
                {
                    var seriesName = item.Serie!.Trim();
                    cardNamesBySeries.TryGetValue(NormalizeLookupKey(seriesName), out var knownCardNames);

                    return new SeriesCatalogItem
                    {
                        SeriesName = seriesName,
                        Year = item.Jahr,
                        SpecialFeatures = item.Besonderheiten ?? Array.Empty<string>(),
                        SpecialEditions = item.Sondereditionen ?? Array.Empty<string>(),
                        KnownCardNames = knownCardNames ?? Array.Empty<string>()
                    };
                })
                .OrderBy(item => item.Year)
                .ThenBy(item => item.SeriesName, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<SeriesCatalogItem>();

            return new CatalogSnapshot
            {
                DataDirectory = dataDirectoryPath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                Series = series
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load catalog from {CatalogPath}", mainCatalogPath);
            return new CatalogSnapshot
            {
                DataDirectory = dataDirectoryPath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                Series = Array.Empty<SeriesCatalogItem>()
            };
        }
    }

    private static Dictionary<string, string[]> LoadSeriesCardNames(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return new Dictionary<string, string[]>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var detailFilePath in Directory.EnumerateFiles(dataDirectory, "series_*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(detailFilePath, Encoding.UTF8));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var names = ExtractCardNames(property.Value);
                    if (names.Length == 0)
                    {
                        continue;
                    }

                    var seriesName = ToSeriesDisplayName(property.Name);
                    result[NormalizeLookupKey(seriesName)] = names;
                }
            }
            catch
            {
                // Ignore invalid detail files and continue with valid files.
            }
        }

        return result;
    }

    private static string[] ExtractCardNames(JsonElement root)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectCardNames(root, names);

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectCardNames(JsonElement element, HashSet<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("Name")
                        && property.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        names.Add(property.Value.GetString()!.Trim());
                    }

                    CollectCardNames(property.Value, names);
                }
                break;

            case JsonValueKind.Array:
                foreach (var child in element.EnumerateArray())
                {
                    CollectCardNames(child, names);
                }
                break;
        }
    }

    private static string ResolveDataDirectory(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["Catalog:Directory"]
            ?? configuration["CATALOG_DIRECTORY"];

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        var contentRootCandidate = Path.Combine(environment.ContentRootPath, "cardInfos");
        if (Directory.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, "cardInfos");
        if (Directory.Exists(baseDirCandidate))
        {
            return baseDirCandidate;
        }

        return contentRootCandidate;
    }

    private static long ComputeCatalogStamp(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return 0;
        }

        var latestTicks = 0L;
        foreach (var filePath in Directory.EnumerateFiles(dataDirectory, "*.json"))
        {
            var ticks = File.GetLastWriteTimeUtc(filePath).Ticks;
            if (ticks > latestTicks)
            {
                latestTicks = ticks;
            }
        }

        return latestTicks;
    }

    private static string ToSeriesDisplayName(string rawSeriesName)
    {
        return MultiWhitespaceRegex().Replace(rawSeriesName.Replace('_', ' ').Trim(), " ");
    }

    private static string NormalizeLookupKey(string value)
    {
        var compact = value.Trim().ToLowerInvariant();
        compact = compact.Replace('_', ' ').Replace('-', ' ');
        return MultiWhitespaceRegex().Replace(compact, " ");
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiWhitespaceRegex();
}
