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
    public int TotalCatalogCards { get; init; }
    public int OwnedCatalogCards { get; init; }
    public int TotalPhotos { get; init; }
    public PhotoAnalysisStatusCounts AnalysisStatusCounts { get; init; } = new();
    public PhotoReviewStatusCounts ReviewStatusCounts { get; init; } = new();
}

internal sealed class PhotoAnalysisStatusCounts
{
    public int Ok { get; init; }
    public int Uncertain { get; init; }
    public int Failed { get; init; }
    public int NotAnalyzed { get; init; }
}

internal sealed class PhotoReviewStatusCounts
{
    public int Unreviewed { get; init; }
    public int Verified { get; init; }
    public int Incorrect { get; init; }
}
