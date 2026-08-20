namespace NinjagoScanner.Web.Shared.Models;

/// <summary>
/// A single scanned card photo and its sidecar data, as surfaced by the BFF. Identified by
/// <see cref="PhotoId"/> (the generated identity PictureService assigns at upload time) rather
/// than by file name; <see cref="SourceFileName"/> is retained only for display.
/// </summary>
public sealed class CardListItem
{
    public required string PhotoId { get; init; }
    public required string SourceFileName { get; init; }
    public required string ImageUrl { get; init; }
    public required string AnalysisStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public string? Language { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public required string ReviewStatus { get; init; }
}
