using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

public enum TradeFinderStatus
{
    Ready,
    /// <summary>Unknown user, not a friend, or the friend's collection is private ("Nur ich").</summary>
    Unavailable
}

/// <summary>Everything the /trade page needs before a partner is chosen.</summary>
public sealed record TradeFinderOverview(
    IReadOnlyList<TradePartnerRank> Partners,
    TradeInventory Mine,
    IReadOnlyList<TradeCatalogCard> Catalog,
    ISet<string> ReservedPhotoIds);

/// <summary>A chosen partner: initial suggestion, the candidate pools for manual swaps and card images.</summary>
public sealed record TradeFinderSession(
    TradeFinderStatus Status,
    string PartnerUserId,
    string PartnerUserName,
    TradeSuggestion Suggestion,
    IReadOnlyList<TradeCardOffer> GiveCandidates,
    IReadOnlyList<TradeCardOffer> ReceiveCandidates,
    IReadOnlyDictionary<string, string> ImageUrls)
{
    public static TradeFinderSession Unavailable(string userName) =>
        new(TradeFinderStatus.Unavailable, string.Empty, userName, TradeSuggestion.Empty, [], [],
            new Dictionary<string, string>());
}

/// <summary>
/// Loads catalog cards and the owned copies of one collection from CatalogService/PictureService.
/// Only ever given collection ids that came from <see cref="FriendAccessService"/> or the
/// viewer's own membership.
/// </summary>
internal sealed class TradeInventoryLoader(
    CatalogServiceClient catalogServiceClient,
    PictureServiceClient pictureServiceClient) : ITradeInventoryLoader
{
    public async Task<IReadOnlyList<TradeCatalogCard>> LoadCatalogAsync(CancellationToken cancellationToken = default) =>
        (await catalogServiceClient.ListCatalogCardsAsync(cancellationToken))
            .Select(c => new TradeCatalogCard(c.Series, c.CardNumber, c.CardName, c.Rarity))
            .ToList();

    public async Task<TradeInventory> LoadAsync(string collectionId, CancellationToken cancellationToken = default) =>
        await LoadAsync(collectionId, await LoadCatalogAsync(cancellationToken), cancellationToken);

    public async Task<TradeInventory> LoadAsync(
        string collectionId, IReadOnlyList<TradeCatalogCard> catalog, CancellationToken cancellationToken = default)
    {
        var byOwnershipKey = new Dictionary<string, TradeCatalogCard>(StringComparer.Ordinal);
        foreach (var card in catalog)
        {
            var key = CollectionQueryService.BuildOwnershipKey(card.Series, card.CardNumber);
            if (!string.IsNullOrEmpty(key))
            {
                byOwnershipKey.TryAdd(key, card);
            }
        }

        var entries = await pictureServiceClient.ListCardEntriesForCollectionAsync(collectionId, cancellationToken);
        var copies = new Dictionary<TradeCardKey, List<TradeOwnedPhoto>>();
        foreach (var entry in entries)
        {
            var key = CollectionQueryService.BuildOwnershipKey(entry.SetName, entry.CardNumber);
            if (string.IsNullOrEmpty(key) || !byOwnershipKey.TryGetValue(key, out var card))
            {
                continue; // unmapped photos are never tradable
            }

            if (!copies.TryGetValue(card.Key, out var list))
            {
                copies[card.Key] = list = [];
            }

            list.Add(new TradeOwnedPhoto(entry.PhotoId, entry.ReviewStatus, entry.AnalysisStatus));
        }

        return new TradeInventory(copies.ToDictionary(p => p.Key, p => (IReadOnlyList<TradeOwnedPhoto>)p.Value));
    }
}

/// <summary>
/// Loads the data behind the /trade finder (web-trade-finder): ranks partners, builds the
/// rarity-balanced suggestion and the candidate pools for manual adjustment. A friend's collection
/// id is only ever obtained from <see cref="FriendAccessService"/>.
/// </summary>
internal sealed class TradeFinderService(
    AppDbContext dbContext,
    TradeInventoryLoader inventoryLoader,
    PictureServiceClient pictureServiceClient,
    FriendAccessService friendAccessService,
    TradePartnerService partnerService)
{
    public async Task<TradeFinderOverview> LoadOverviewAsync(string userId, CancellationToken cancellationToken = default)
    {
        var catalog = await inventoryLoader.LoadCatalogAsync(cancellationToken);
        var collectionId = await GetOwnedCollectionIdAsync(userId, cancellationToken);
        var mine = collectionId is null
            ? TradeInventory.Empty
            : await inventoryLoader.LoadAsync(collectionId, catalog, cancellationToken);
        var reserved = await LoadReservedPhotoIdsAsync(cancellationToken);
        var partners = await partnerService.RankPartnersAsync(userId, mine, catalog, reserved, cancellationToken);
        return new TradeFinderOverview(partners, mine, catalog, reserved);
    }

    public async Task<TradeFinderSession> LoadSessionAsync(
        string userId, string? partnerUserName, CancellationToken cancellationToken = default)
    {
        var access = await friendAccessService.ResolveVisibleCollectionAsync(userId, partnerUserName, cancellationToken);
        var myCollectionId = await GetOwnedCollectionIdAsync(userId, cancellationToken);
        if (access.Status != FriendAccessStatus.Visible || access.CollectionId is null
            || access.OwnerUserId is null || myCollectionId is null)
        {
            return TradeFinderSession.Unavailable(partnerUserName ?? string.Empty);
        }

        var catalog = await inventoryLoader.LoadCatalogAsync(cancellationToken);
        var mine = await inventoryLoader.LoadAsync(myCollectionId, catalog, cancellationToken);
        var theirs = await inventoryLoader.LoadAsync(access.CollectionId, catalog, cancellationToken);
        var reserved = await LoadReservedPhotoIdsAsync(cancellationToken);

        var give = TradeMatchingService.GetOfferableTo(mine, theirs, catalog, reserved)
            .Select(ToOffer).ToList();
        var receive = TradeMatchingService.GetOfferableTo(theirs, mine, catalog, reserved)
            .Select(ToOffer).ToList();
        var suggestion = TradeMatchingService.Pair(give, receive);

        var urls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, url) in await pictureServiceClient.GetDownloadUrlsForCollectionAsync(
                     myCollectionId, give.Select(o => o.PhotoId), cancellationToken))
        {
            urls[id] = url;
        }

        foreach (var (id, url) in await pictureServiceClient.GetDownloadUrlsForCollectionAsync(
                     access.CollectionId, receive.Select(o => o.PhotoId), cancellationToken))
        {
            urls[id] = url;
        }

        return new TradeFinderSession(
            TradeFinderStatus.Ready, access.OwnerUserId, access.OwnerUserName ?? partnerUserName ?? string.Empty,
            suggestion, give, receive, urls);
    }

    private static TradeCardOffer ToOffer(TradableCard t) =>
        new(t.OfferedPhoto.PhotoId, t.Card.Series, t.Card.CardNumber, t.Card.CardName,
            Models.CardRarity.Normalize(t.Card.Rarity));

    /// <summary>Photos locked in open (pending or executing) trades.</summary>
    private async Task<ISet<string>> LoadReservedPhotoIdsAsync(CancellationToken cancellationToken)
    {
        var ids = await dbContext.TradeItems
            .AsNoTracking()
            .Join(dbContext.Trades, item => item.TradeId, trade => trade.Id, (item, trade) => new { item, trade })
            .Where(x => x.trade.Status == TradeStatus.Pending || x.trade.Status == TradeStatus.Executing)
            .Select(x => x.item.PhotoId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    private Task<string?> GetOwnedCollectionIdAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.CollectionMemberships
            .Where(m => m.UserId == userId && m.Role == CollectionRole.Owner)
            .Select(m => (string?)m.CollectionId)
            .FirstOrDefaultAsync(cancellationToken);
}
