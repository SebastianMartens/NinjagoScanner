using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinjagoScanner.CatalogService.Catalog;

public sealed partial class CatalogRepository(ILogger<CatalogRepository> logger, IWebHostEnvironment environment, IConfiguration configuration)
{
    private readonly object gate = new();
    private CatalogSnapshot? cachedSnapshot;
    private long cachedStamp;

    private readonly string dataDirectoryPath = ResolveDataDirectory(environment, configuration);

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

    public SeriesMetadataItem? FindSeriesMetadata(string seriesName)
    {
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            return null;
        }

        var key = NormalizeLookupKey(seriesName);
        return GetSnapshot().MetadataBySeriesKey.TryGetValue(key, out var metadata)
            ? metadata
            : null;
    }

    private CatalogSnapshot LoadSnapshot()
    {
        try
        {
            var detailData = LoadSeriesDetails(dataDirectoryPath);
            var series = BuildSeriesList(detailData);
            var cards = detailData.Values
                .SelectMany(entry => entry.Cards)
                .OrderBy(card => card.SortOrder)
                .ThenBy(card => ToSortKey(card.CardNumber), StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.CardName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var metadataBySeries = detailData
                .Values
                .Where(entry => entry.Metadata is not null)
                .ToDictionary(
                    entry => NormalizeLookupKey(entry.SeriesName),
                    entry => entry.Metadata!,
                    StringComparer.Ordinal);

            return new CatalogSnapshot
            {
                DataDirectory = dataDirectoryPath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                Series = series,
                Cards = cards,
                MetadataBySeriesKey = metadataBySeries
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load catalog from {CatalogPath}", dataDirectoryPath);
            return new CatalogSnapshot
            {
                DataDirectory = dataDirectoryPath,
                LoadedAtUtc = DateTimeOffset.UtcNow,
                Series = Array.Empty<SeriesCatalogItem>(),
                Cards = Array.Empty<CatalogCardItem>(),
                MetadataBySeriesKey = new Dictionary<string, SeriesMetadataItem>(StringComparer.Ordinal)
            };
        }
    }

    private static SeriesCatalogItem[] BuildSeriesList(IReadOnlyDictionary<string, SeriesDetailData> detailData)
    {
        return detailData.Values
            .Select(detail => new SeriesCatalogItem
            {
                SeriesName = detail.SeriesName,
                Year = detail.Metadata?.Year ?? 0,
                SortOrder = detail.Metadata?.SortOrder ?? 0,
                SpecialFeatures = detail.Metadata?.Highlights ?? Array.Empty<string>(),
                SpecialEditions = detail.Metadata?.SpecialEditions ?? Array.Empty<string>(),
                KnownCardNames = detail.KnownCardNames
            })
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static Dictionary<string, SeriesDetailData> LoadSeriesDetails(string dataDirectory)
    {
        var result = new Dictionary<string, SeriesDetailData>(StringComparer.Ordinal);
        if (!Directory.Exists(dataDirectory))
        {
            return result;
        }

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

                    var seriesName = ToSeriesDisplayName(property.Name);
                    var seriesKey = NormalizeLookupKey(seriesName);
                    var metadata = ExtractSeriesMetadata(seriesName, property.Value);
                    var cards = ExtractSeriesCards(seriesName, metadata.SortOrder ?? 0, property.Value);
                    var knownCardNames = cards
                        .Select(card => card.CardName)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    result[seriesKey] = new SeriesDetailData
                    {
                        SeriesName = seriesName,
                        Metadata = metadata,
                        Cards = cards,
                        KnownCardNames = knownCardNames
                    };
                }
            }
            catch
            {
                // Ignore malformed detail files and continue.
            }
        }

        return result;
    }

    private static SeriesMetadataItem ExtractSeriesMetadata(string seriesName, JsonElement seriesRoot)
    {
        return new SeriesMetadataItem
        {
            SeriesName = seriesName,
            Year = seriesRoot.TryGetProperty("Jahr", out var yearProperty) && yearProperty.ValueKind == JsonValueKind.Number
                ? yearProperty.GetInt32()
                : null,
            SortOrder = seriesRoot.TryGetProperty("SortOrder", out var sortOrderProperty) && sortOrderProperty.ValueKind == JsonValueKind.Number
                ? sortOrderProperty.GetInt32()
                : null,
            Logo = seriesRoot.TryGetProperty("Logo", out var logoProperty) && logoProperty.ValueKind == JsonValueKind.String
                ? logoProperty.GetString()
                : null,
            Theme = seriesRoot.TryGetProperty("Thema", out var themeProperty) && themeProperty.ValueKind == JsonValueKind.String
                ? themeProperty.GetString()
                : null,
            Highlights = seriesRoot.TryGetProperty("Besonderheiten", out var highlightsProperty) && highlightsProperty.ValueKind == JsonValueKind.Array
                ? highlightsProperty.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray()
                : Array.Empty<string>(),
            SpecialEditions = seriesRoot.TryGetProperty("Sondereditionen", out var specialEditionsProperty) && specialEditionsProperty.ValueKind == JsonValueKind.Array
                ? specialEditionsProperty.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray()
                : Array.Empty<string>()
        };
    }

    private static CatalogCardItem[] ExtractSeriesCards(string seriesName, int sortOrder, JsonElement seriesRoot)
    {
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cards = new List<CatalogCardItem>();

        foreach (var entry in EnumerateCardEntries(seriesRoot, []))
        {
            var normalizedNumber = NormalizeCardNumber(entry.CardNumber);
            if (string.IsNullOrWhiteSpace(normalizedNumber) || string.IsNullOrWhiteSpace(entry.CardName))
            {
                continue;
            }

            var category = string.IsNullOrWhiteSpace(entry.Category)
                ? "Unkategorisiert"
                : entry.Category.Trim();
            var cardName = entry.CardName.Trim();
            var uniqueKey = string.Join('|', seriesName, category, normalizedNumber, cardName);
            if (!uniqueKeys.Add(uniqueKey))
            {
                continue;
            }

            cards.Add(new CatalogCardItem
            {
                SeriesName = seriesName,
                Category = category,
                CardNumber = normalizedNumber,
                CardName = cardName,
                SortOrder = sortOrder
            });
        }

        return cards.ToArray();
    }

    private static IEnumerable<(string CardNumber, string CardName, string Category)> EnumerateCardEntries(
        JsonElement element,
        IReadOnlyList<string> categoryPath)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Karten-Nr.", out var numberProperty)
                && element.TryGetProperty("Name", out var nameProperty)
                && numberProperty.ValueKind != JsonValueKind.Object
                && nameProperty.ValueKind == JsonValueKind.String)
            {
                var number = numberProperty.ValueKind switch
                {
                    JsonValueKind.Number => numberProperty.GetRawText(),
                    JsonValueKind.String => numberProperty.GetString(),
                    _ => null
                };

                var name = nameProperty.GetString();
                if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(name))
                {
                    yield return (number, name, BuildCategoryLabel(categoryPath));
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("Karten-Nr.") || property.NameEquals("Name"))
                {
                    continue;
                }

                var nextCategoryPath = categoryPath;
                if (ShouldTrackCategory(property.Name))
                {
                    nextCategoryPath = [.. categoryPath, ToCategoryDisplayName(property.Name)];
                }

                foreach (var entry in EnumerateCardEntries(property.Value, nextCategoryPath))
                {
                    yield return entry;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var entry in EnumerateCardEntries(item, categoryPath))
                {
                    yield return entry;
                }
            }
        }
    }

    private static bool ShouldTrackCategory(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var normalized = propertyName.Trim();
        return !normalized.Equals("Jahr", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("SortOrder", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Logo", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Thema", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Besonderheiten", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Sondereditionen", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Kategorien", StringComparison.OrdinalIgnoreCase)
               && !normalized.StartsWith("Serie", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToCategoryDisplayName(string rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
        {
            return "Unkategorisiert";
        }

        var normalized = rawCategory.Trim().Replace('_', ' ');
        return MultiWhitespaceRegex().Replace(normalized, " ").Trim();
    }

    private static string BuildCategoryLabel(IReadOnlyList<string> categoryPath)
    {
        return categoryPath.Count == 0
            ? "Unkategorisiert"
            : string.Join(" / ", categoryPath);
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

    private static string NormalizeCardNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        normalized = NonAlphaNumericRegex().Replace(normalized, string.Empty);

        if (NumberOnlyRegex().IsMatch(normalized) && int.TryParse(normalized, out var numericValue))
        {
            return numericValue.ToString();
        }

        return normalized;
    }

    private static string ToSortKey(string cardNumber)
    {
        if (int.TryParse(cardNumber, out var numericValue))
        {
            return $"0-{numericValue:D6}";
        }

        var match = AlphaPrefixNumberRegex().Match(cardNumber);
        if (match.Success && int.TryParse(match.Groups["number"].Value, out var suffixValue))
        {
            return $"1-{match.Groups["prefix"].Value}-{suffixValue:D6}";
        }

        return $"9-{cardNumber}";
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("^\\d+$")]
    private static partial Regex NumberOnlyRegex();

    [GeneratedRegex("^(?<prefix>[A-Z]+)(?<number>\\d+)$")]
    private static partial Regex AlphaPrefixNumberRegex();

    private sealed class SeriesDetailData
    {
        public required string SeriesName { get; init; }
        public required CatalogCardItem[] Cards { get; init; }
        public required string[] KnownCardNames { get; init; }
        public SeriesMetadataItem? Metadata { get; init; }
    }
}
