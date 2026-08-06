namespace NinjagoScanner.Web.Models;

internal static class ReviewStatuses
{
    public const string Unreviewed = "unreviewed";
    public const string Verified = "verified";
    public const string Incorrect = "incorrect";
}

internal sealed class CardListItem
{
    public required string ImageFileName { get; init; }
    public required string ImageUrl { get; init; }
    public required string AnalysisStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public required string ReviewStatus { get; init; }
}