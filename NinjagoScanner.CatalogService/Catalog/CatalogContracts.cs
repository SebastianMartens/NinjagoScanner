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
    public int SortOrder { get; init; }
    public string[] SpecialFeatures { get; init; } = Array.Empty<string>();
    public string[] SpecialEditions { get; init; } = Array.Empty<string>();
    public string[] KnownCardNames { get; init; } = Array.Empty<string>();
}

public sealed class CatalogCardItem
{
    public required string SeriesName { get; init; }
    public required string Category { get; init; }
    public required string Class { get; init; }
    public required string Rarity { get; init; }
    public required string CardNumber { get; init; }
    public required string CardName { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// Thrown when catalog data is structurally valid JSON but violates the catalog data contract.
/// Unlike malformed JSON (which is skipped per file), this fails catalog loading.
/// </summary>
public sealed class CatalogDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// The catalog's fixed set of card rarities. Declared per category (and optionally per card) in
/// the series data; see <c>catalog-service-card-catalog</c>.
/// </summary>
public static class CatalogRarities
{
    public const string Common = "common";
    public const string Limited = "limited";
    public const string Legendary = "legendary";

    public static readonly IReadOnlyList<string> All = [Common, Limited, Legendary];

    /// <summary>Returns the normalized rarity, or throws <see cref="CatalogDataException"/> for a value outside the set.</summary>
    public static string Validate(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return All.Contains(normalized)
            ? normalized
            : throw new CatalogDataException($"Unknown Rarity '{value}'; expected one of: {string.Join(", ", All)}.");
    }
}

public sealed class SeriesMetadataItem
{
    public required string SeriesName { get; init; }
    public int? Year { get; init; }
    public int? SortOrder { get; init; }
    public string? Logo { get; init; }
    public string? Theme { get; init; }
    public string[] Highlights { get; init; } = Array.Empty<string>();
    public string[] SpecialEditions { get; init; } = Array.Empty<string>();
}
