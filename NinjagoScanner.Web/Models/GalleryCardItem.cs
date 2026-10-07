namespace NinjagoScanner.Web.Models;

public sealed class GalleryCardItem
{
    public required string Series { get; init; }
    public int SortOrder { get; init; }
    public required string Category { get; init; }
    public required string CardNumber { get; init; }
    public required string CardName { get; init; }
    public string? PhotoId { get; init; }
    public string? ImageUrl { get; init; }
    public int PhotoCount { get; init; }
    /// <summary>The catalog card's rarity (not a photo's).</summary>
    public string Rarity { get; init; } = CardRarity.Common;
    public string? ReviewStatus { get; init; }
}
