namespace NinjagoScanner.Web.Models;

/// <summary>
/// A single scanned card photo and its sidecar data. Identified by <see cref="PhotoId"/> (the
/// generated identity PictureService assigns at upload time) rather than by file name;
/// <see cref="SourceFileName"/> is retained only for display. A record so a changed photo can be
/// produced with <c>with</c> while every unchanged field (notably <see cref="ImageUrl"/>) is kept.
/// </summary>
public sealed record CardListItem
{
    public required string PhotoId { get; init; }
    public required string SourceFileName { get; init; }

    /// <summary>
    /// The photo's short-lived download URL, or empty until a caller resolves it (listing a
    /// collection resolves none - only the photos a page displays get one).
    /// </summary>
    public string ImageUrl { get; init; } = string.Empty;
    public required string AnalysisStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public string? Language { get; init; }
    public required string ReviewStatus { get; init; }
}

/// <summary>
/// The sidecar fields not carried on <see cref="CardListItem"/>, resolved on demand via
/// <see cref="NinjagoScanner.Web.Services.PictureServiceClient.GetCardDetailsAsync"/> for a single photo shown in an
/// expanded/selected detail view. <see cref="AttributesJson"/> is the staged analysis pipeline's
/// raw Detected/Derived attribute maps as JSON, for debugging/quality checks.
/// </summary>
public sealed class CardDetailsItem
{
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AttributesJson { get; init; }
}
