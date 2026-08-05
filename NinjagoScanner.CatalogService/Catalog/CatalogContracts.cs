using System.Text.Json.Serialization;

namespace NinjagoScanner.CatalogService.Catalog;

public sealed class CatalogSnapshot
{
    public required string DataDirectory { get; init; }
    public required DateTimeOffset LoadedAtUtc { get; init; }
    public IReadOnlyList<SeriesCatalogItem> Series { get; init; } = Array.Empty<SeriesCatalogItem>();
    public IReadOnlyList<CatalogCardItem> Cards { get; init; } = Array.Empty<CatalogCardItem>();
    public IReadOnlyDictionary<string, SeriesMetadataItem> MetadataBySeriesKey { get; init; } =
        new Dictionary<string, SeriesMetadataItem>(StringComparer.Ordinal);
}

public sealed class SeriesCatalogItem
{
    public required string SeriesName { get; init; }
    public int Year { get; init; }
    public string[] SpecialFeatures { get; init; } = Array.Empty<string>();
    public string[] SpecialEditions { get; init; } = Array.Empty<string>();
    public string[] KnownCardNames { get; init; } = Array.Empty<string>();
}

public sealed class CatalogCardItem
{
    public required string SeriesName { get; init; }
    public required string Category { get; init; }
    public required string CardNumber { get; init; }
    public required string CardName { get; init; }
}

public sealed class SeriesMetadataItem
{
    public required string SeriesName { get; init; }
    public int? Year { get; init; }
    public string? Logo { get; init; }
    public string? Theme { get; init; }
    public string[] Highlights { get; init; } = Array.Empty<string>();
}

internal sealed class SeriesCatalogRoot
{
    [JsonPropertyName("Ninjago_Sammelkarten_Serien")]
    public SeriesCatalogJsonItem[]? Series { get; init; }
}

internal sealed class SeriesCatalogJsonItem
{
    [JsonPropertyName("Serie")]
    public string? Serie { get; init; }

    [JsonPropertyName("Jahr")]
    public int Jahr { get; init; }

    [JsonPropertyName("Besonderheiten")]
    public string[]? Besonderheiten { get; init; }

    [JsonPropertyName("Sondereditionen")]
    public string[]? Sondereditionen { get; init; }
}
