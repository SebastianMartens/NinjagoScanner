namespace NinjagoScanner.Web.Models;

/// <summary>
/// A set of photos sharing the same (SetName, CardNumber) pair from their own sidecar data,
/// as shown one at a time on the /review page. <see cref="IsCatchAll"/> groups are the single
/// trailing bucket for photos whose SetName does not match a known catalog series (including blank).
/// </summary>
internal sealed class CardReviewGroup
{
    public required bool IsCatchAll { get; init; }
    public string? SeriesName { get; init; }
    public string? CardNumber { get; init; }
    public required IReadOnlyList<CardListItem> Photos { get; init; }

    public string Key => IsCatchAll
        ? "__unassigned__"
        : $"{SeriesName}␟{CardNumber}";
}
