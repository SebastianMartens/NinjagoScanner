namespace NinjagoScanner.Web.Services;

/// <summary>Loads the owned copies of one collection. Implementations must only be given collection IDs
/// that came from <see cref="FriendAccessService"/> (or the viewer's own collection).</summary>
public interface ITradeInventoryLoader
{
    Task<TradeInventory> LoadAsync(string collectionId, CancellationToken cancellationToken = default);
}

/// <param name="OfferCount">My tradable cards this friend wants.</param>
/// <param name="WantCount">The friend's tradable cards I want.</param>
/// <param name="ExchangeableCount">Cards that can actually be swapped: min(OfferCount, WantCount).</param>
public sealed record TradePartnerRank(
    string UserId, string UserName, int OfferCount, int WantCount, int ExchangeableCount, bool IsAvailable);

/// <summary>Ranks the viewer's friends as trade partners (web-trade-finder, "Partner ranking").</summary>
public sealed class TradePartnerService(
    FriendService friendService,
    FriendAccessService friendAccessService,
    ITradeInventoryLoader inventoryLoader)
{
    public async Task<IReadOnlyList<TradePartnerRank>> RankPartnersAsync(
        string viewerUserId,
        TradeInventory mine,
        IReadOnlyList<TradeCatalogCard> catalog,
        ISet<string>? myReservedPhotoIds = null,
        CancellationToken cancellationToken = default)
    {
        var friends = (await friendService.ListAsync(viewerUserId, cancellationToken)).Friends;
        var candidates = new List<TradePartnerRank>();
        foreach (var friend in friends)
        {
            var access = await friendAccessService.ResolveVisibleCollectionAsync(
                viewerUserId, friend.UserName, cancellationToken);
            if (access.Status != FriendAccessStatus.Visible || access.CollectionId is null)
            {
                candidates.Add(new TradePartnerRank(friend.UserId, friend.UserName, 0, 0, 0, false));
                continue;
            }

            var theirs = await inventoryLoader.LoadAsync(access.CollectionId, cancellationToken);
            candidates.Add(Rank(friend.UserId, friend.UserName, mine, theirs, catalog, myReservedPhotoIds));
        }

        return Order(candidates);
    }

    public static TradePartnerRank Rank(
        string userId, string userName, TradeInventory mine, TradeInventory theirs,
        IReadOnlyList<TradeCatalogCard> catalog, ISet<string>? myReservedPhotoIds = null)
    {
        var offer = TradeMatchingService.GetOfferableTo(mine, theirs, catalog, myReservedPhotoIds).Count;
        var want = TradeMatchingService.GetOfferableTo(theirs, mine, catalog).Count;
        return new TradePartnerRank(userId, userName, offer, want, Math.Min(offer, want), true);
    }

    /// <summary>Available partners by exchangeable count desc (then total overlap, then name), unavailable last.</summary>
    public static IReadOnlyList<TradePartnerRank> Order(IEnumerable<TradePartnerRank> ranks) => ranks
        .OrderByDescending(r => r.IsAvailable)
        .ThenByDescending(r => r.ExchangeableCount)
        .ThenByDescending(r => r.OfferCount + r.WantCount)
        .ThenBy(r => r.UserName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
