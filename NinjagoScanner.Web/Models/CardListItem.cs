namespace NinjagoScanner.Web.Models;

internal static class AnalysisStatuses
{
    public const string Ok = "ok";
    public const string Uncertain = "uncertain";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

internal static class ReviewStatuses
{
    public const string Unreviewed = "unreviewed";
    public const string Verified = "verified";
    public const string Incorrect = "incorrect";
}

internal static class Languages
{
    public const string German = "de";
    public const string English = "en";
    public const string Polish = "pl";
    public const string Unknown = "unknown";
    public const string Default = German;
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
    public string? Language { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public required string ReviewStatus { get; init; }
}