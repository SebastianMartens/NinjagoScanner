namespace NinjagoScanner.Web.Models;

internal sealed class SeriesSummaryItem
{
    public required string SeriesName { get; init; }
    public int SortOrder { get; init; }
    public int TotalCards { get; init; }
    public int OwnedCards { get; init; }
    public int TotalPhotos { get; init; }
}

internal sealed class SeriesSummaryResult
{
    public required IReadOnlyList<SeriesSummaryItem> Series { get; init; }
    public int UnknownSeriesPhotoCount { get; init; }
}
