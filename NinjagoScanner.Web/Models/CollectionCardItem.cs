namespace NinjagoScanner.Web.Models;

internal sealed class CollectionCardItem
{
    public required string Series { get; init; }
    public int SortOrder { get; init; }
    public required string Category { get; init; }
    public required string CardNumber { get; init; }
    public required string CardName { get; init; }
    public int OwnedCopies { get; init; }

    public bool IsOwned => OwnedCopies > 0;
    public bool IsDuplicateOwned => OwnedCopies > 1;
}

internal sealed class CollectionOverviewResult
{
    public required IReadOnlyList<CollectionCardItem> Cards { get; init; }
    public int TotalPhotos { get; init; }
    public int MappedPhotos { get; init; }
    public int UnmappedPhotos => Math.Max(0, TotalPhotos - MappedPhotos);
}