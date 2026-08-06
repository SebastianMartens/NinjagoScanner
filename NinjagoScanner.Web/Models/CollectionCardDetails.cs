namespace NinjagoScanner.Web.Models;

internal sealed class CollectionCardDetails
{
    public required string Series { get; init; }
    public required string Category { get; init; }
    public required string CardNumber { get; init; }
    public required string CardName { get; init; }
    public int? Year { get; init; }
    public string? Logo { get; init; }
    public string? Theme { get; init; }
    public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
    public IReadOnlyList<CollectionCardPhotoItem> Photos { get; init; } = Array.Empty<CollectionCardPhotoItem>();
}

internal sealed class CollectionCardPhotoItem
{
    public required string ImageFileName { get; init; }
    public required string ImageUrl { get; init; }
    public CollectionCardSidecarData? Sidecar { get; init; }
}

internal sealed class CollectionCardSidecarData
{
    public string? AnalysisStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ReviewStatus { get; init; }
}

internal sealed class CollectionCardSidecarUpdate
{
    public string? AnalysisStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public string? ErrorMessage { get; init; }
    public string? ReviewStatus { get; init; }
}