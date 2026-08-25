namespace NinjagoScanner.Web.Models;

/// <summary>
/// A set of photos whose sidecar SetName/CardNumber resolve, after normalization, to the same
/// catalog card, as shown one at a time on the /review page. <see cref="IsCatchAll"/> groups are
/// the single trailing bucket for photos whose SetName/CardNumber do not resolve to any catalog
/// card (including a blank SetName or CardNumber).
/// </summary>
public sealed class CardReviewGroup
{
    public required bool IsCatchAll { get; init; }
    public string? SeriesName { get; init; }
    public string? CardNumber { get; init; }
    public string? CardName { get; init; }
    public required IReadOnlyList<CardListItem> Photos { get; init; }

    public string Key => IsCatchAll
        ? "__unassigned__"
        : $"{SeriesName}␟{CardNumber}";
}
