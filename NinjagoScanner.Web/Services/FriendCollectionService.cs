using Microsoft.EntityFrameworkCore;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// The only PictureService surface the friend view can reach: read-only listing and download
/// URLs for a named collection. Deliberately has no update/delete/upload/rotation members, so a
/// foreign collection can never be modified through <see cref="FriendCollectionService"/>.
/// </summary>
public interface IForeignCollectionReader
{
    Task<IReadOnlyList<CardEntry>> ListCardEntriesAsync(string collectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetDownloadUrlsAsync(
        string collectionId, IEnumerable<string> photoIds, CancellationToken cancellationToken = default);
}

internal sealed class PictureServiceForeignCollectionReader(PictureServiceClient pictureServiceClient) : IForeignCollectionReader
{
    public Task<IReadOnlyList<CardEntry>> ListCardEntriesAsync(string collectionId, CancellationToken cancellationToken = default) =>
        pictureServiceClient.ListCardEntriesForCollectionAsync(collectionId, cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> GetDownloadUrlsAsync(
        string collectionId, IEnumerable<string> photoIds, CancellationToken cancellationToken = default) =>
        pictureServiceClient.GetDownloadUrlsForCollectionAsync(collectionId, photoIds, cancellationToken);
}

/// <param name="Shared">Catalog cards both the viewer and the friend own.</param>
/// <param name="FriendOnly">Catalog cards only the friend owns.</param>
/// <param name="ViewerOnly">Catalog cards only the viewer owns.</param>
public sealed record FriendComparison(int Shared, int FriendOnly, int ViewerOnly);

public sealed class FriendCollectionView
{
    public required FriendAccessStatus Status { get; init; }
    public string? OwnerUserName { get; init; }
    public SeriesSummaryResult? Summary { get; init; }
    public int Xp { get; init; }
    public RankDefinition? Rank { get; init; }
    public IReadOnlyList<AchievementProgress> Achievements { get; init; } = [];
    public FriendComparison? Comparison { get; init; }
    public IReadOnlyList<string> SeriesNames { get; init; } = [];
}

/// <summary>
/// Read-only facade for <c>/friends/{username}</c> over catalog, picture and gamification reads.
/// Every call first goes through <see cref="FriendAccessService"/>; the friend's collection id is
/// used only if it comes back from there. It never writes unlock rows, never touches
/// <see cref="GamificationCelebrationCenter"/> and only holds an <see cref="IForeignCollectionReader"/>.
/// </summary>
internal sealed class FriendCollectionService(
    FriendAccessService friendAccessService,
    CatalogServiceClient catalogServiceClient,
    IForeignCollectionReader reader,
    AppDbContext dbContext)
{
    public async Task<FriendCollectionView> GetViewAsync(
        string viewerUserId, string? username, CancellationToken cancellationToken = default)
    {
        var access = await friendAccessService.ResolveVisibleCollectionAsync(viewerUserId, username, cancellationToken);
        if (access.Status != FriendAccessStatus.Visible || access.CollectionId is null)
        {
            return new FriendCollectionView
            {
                Status = access.Status,
                OwnerUserName = access.Status == FriendAccessStatus.Private ? access.OwnerUserName : null
            };
        }

        var catalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var friendEntries = await reader.ListCardEntriesAsync(access.CollectionId, cancellationToken);
        var summary = CollectionQueryService.BuildSeriesSummary(catalog, friendEntries);

        var state = GamificationService.BuildStateFromEntries(catalog, friendEntries);
        var bonusXp = await dbContext.GamificationProfiles.AsNoTracking()
            .Where(profile => profile.CollectionId == access.CollectionId)
            .Select(profile => profile.BonusXp)
            .FirstOrDefaultAsync(cancellationToken);
        var xp = GamificationService.ComputeXpForState(state, bonusXp);
        var unlockDates = await dbContext.AchievementUnlocks.AsNoTracking()
            .Where(unlock => unlock.CollectionId == access.CollectionId)
            .ToDictionaryAsync(unlock => unlock.AchievementId, unlock => unlock.UnlockedAtUtc, cancellationToken);

        var viewerCollectionId = await dbContext.CollectionMemberships
            .Where(m => m.UserId == viewerUserId && m.Role == CollectionRole.Owner)
            .Select(m => (string?)m.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);
        IReadOnlyList<CardEntry> viewerEntries = viewerCollectionId is null
            ? []
            : await reader.ListCardEntriesAsync(viewerCollectionId, cancellationToken);

        return new FriendCollectionView
        {
            Status = FriendAccessStatus.Visible,
            OwnerUserName = access.OwnerUserName,
            Summary = summary,
            Xp = xp,
            Rank = RankDefinitions.ForXp(xp),
            Achievements = GamificationService.BuildProgress(state, unlockDates),
            Comparison = Compare(catalog, viewerEntries, friendEntries),
            SeriesNames = catalog.OrderBy(c => c.SortOrder).Select(c => c.Series).Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    /// <summary>Gallery of one series of the friend's collection, owned cards only. Same access check as <see cref="GetViewAsync"/>; null when access is not granted.</summary>
    public async Task<IReadOnlyList<GalleryCardItem>?> GetOwnedGalleryAsync(
        string viewerUserId, string? username, string series, CancellationToken cancellationToken = default)
    {
        var access = await friendAccessService.ResolveVisibleCollectionAsync(viewerUserId, username, cancellationToken);
        if (access.Status != FriendAccessStatus.Visible || access.CollectionId is null)
        {
            return null;
        }

        var catalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var entries = await reader.ListCardEntriesAsync(access.CollectionId, cancellationToken);
        var owned = CollectionQueryService.SelectGalleryCards(catalog, entries, series)
            .Where(match => match.MatchedPhoto is not null)
            .ToArray();
        var urls = await reader.GetDownloadUrlsAsync(
            access.CollectionId, owned.Select(match => match.MatchedPhoto!.PhotoId), cancellationToken);
        return CollectionQueryService.BuildGalleryItems(owned, urls);
    }

    internal static FriendComparison Compare(
        IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder, string Rarity)> catalog,
        IReadOnlyList<CardEntry> viewerEntries,
        IReadOnlyList<CardEntry> friendEntries)
    {
        var mine = OwnedKeys(catalog, viewerEntries);
        var theirs = OwnedKeys(catalog, friendEntries);
        var shared = mine.Count(theirs.Contains);
        return new FriendComparison(shared, theirs.Count - shared, mine.Count - shared);
    }

    private static HashSet<string> OwnedKeys(
        IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder, string Rarity)> catalog,
        IReadOnlyList<CardEntry> entries)
    {
        var photoKeys = entries
            .Select(entry => CollectionQueryService.BuildOwnershipKey(entry.SetName, entry.CardNumber))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        return catalog
            .Select(card => CollectionQueryService.BuildOwnershipKey(card.Series, card.CardNumber))
            .Where(photoKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }
}
