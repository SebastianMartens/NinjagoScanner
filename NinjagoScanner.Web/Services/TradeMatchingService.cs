using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>Catalog card identity (series + card number), case-insensitive.</summary>
public readonly record struct TradeCardKey
{
    public string Series { get; }
    public string CardNumber { get; }

    public TradeCardKey(string series, string cardNumber)
    {
        Series = (series ?? "").Trim().ToUpperInvariant();
        CardNumber = (cardNumber ?? "").Trim().ToUpperInvariant();
    }
}

/// <summary>A catalog card as relevant to trading.</summary>
public sealed record TradeCatalogCard(string Series, string CardNumber, string CardName, string Rarity)
{
    public TradeCardKey Key => new(Series, CardNumber);
}

/// <summary>One owned photo mapped to a catalog card.</summary>
public sealed record TradeOwnedPhoto(
    string PhotoId,
    string ReviewStatus = ReviewStatuses.Unreviewed,
    string AnalysisStatus = AnalysisStatuses.Ok);

/// <summary>
/// A collection's owned copies: catalog card -> photos mapped to it. Unmapped photos are simply
/// not part of the map, and entries whose key is not in the catalog are ignored when matching.
/// </summary>
public sealed class TradeInventory(IReadOnlyDictionary<TradeCardKey, IReadOnlyList<TradeOwnedPhoto>> copies)
{
    public static readonly TradeInventory Empty = new(new Dictionary<TradeCardKey, IReadOnlyList<TradeOwnedPhoto>>());

    public IReadOnlyDictionary<TradeCardKey, IReadOnlyList<TradeOwnedPhoto>> Copies { get; } = copies;

    public int CopyCount(TradeCardKey key) => Copies.TryGetValue(key, out var photos) ? photos.Count : 0;
}

/// <summary>A card (and the concrete photo, when it is on the giving side) in a suggestion.</summary>
public sealed record TradeCardOffer(
    string PhotoId, string Series, string CardNumber, string CardName, string Rarity)
{
    public TradeCardKey Key => new(Series, CardNumber);
}

/// <param name="WeightDifference">Give weight minus receive weight (negative: I give less value).</param>
/// <param name="Unausgewogen">Rarity tiers differ, i.e. the pair is flagged "Unausgewogen".</param>
public sealed record TradePair(TradeCardOffer Give, TradeCardOffer Receive, int WeightDifference, bool Unausgewogen);

public sealed record TradeBalance(
    int GiveCount, int ReceiveCount, int GiveWeight, int ReceiveWeight, int UnausgewogenCount)
{
    public int WeightDifference => GiveWeight - ReceiveWeight;
    public bool IsEqualCount => GiveCount == ReceiveCount;
    public bool IsBalanced => IsEqualCount && UnausgewogenCount == 0;
}

public sealed record TradeSuggestion(IReadOnlyList<TradePair> Pairs, TradeBalance Balance)
{
    public static readonly TradeSuggestion Empty = new([], new TradeBalance(0, 0, 0, 0, 0));
}

/// <summary>A tradable card: one copy may be offered (a partner needs at most one).</summary>
public sealed record TradableCard(TradeCatalogCard Card, int Surplus, TradeOwnedPhoto OfferedPhoto);

/// <summary>
/// Pure, deterministic trade matching (design D4): no I/O. Surplus = copies - 1; the worst-quality
/// copy is offered first so the owner keeps the best one. Cards are paired by equal rarity tier
/// first, leftovers by closest weight (common 1, limited 3, legendary 9), always equal counts.
/// </summary>
public static class TradeMatchingService
{
    public static int Weight(string? rarity) => CardRarity.Normalize(rarity) switch
    {
        CardRarity.Legendary => 9,
        CardRarity.Limited => 3,
        _ => 1
    };

    private static int Tier(string? rarity) => CardRarity.Normalize(rarity) switch
    {
        CardRarity.Legendary => 2,
        CardRarity.Limited => 1,
        _ => 0
    };

    /// <summary>Higher is better; the best copy is the one the owner keeps.</summary>
    private static int Quality(TradeOwnedPhoto photo) => photo.ReviewStatus switch
    {
        ReviewStatuses.Verified => 3,
        ReviewStatuses.Incorrect => 0,
        _ => photo.AnalysisStatus is AnalysisStatuses.Ok ? 2 : 1
    };

    /// <summary>
    /// Cards a user could give away: more than one copy (not counting photos reserved in pending
    /// trades), mapped to a catalog card. Ordered by series then card number.
    /// </summary>
    public static IReadOnlyList<TradableCard> GetTradableCards(
        TradeInventory inventory,
        IEnumerable<TradeCatalogCard> catalog,
        ISet<string>? reservedPhotoIds = null)
    {
        var result = new List<TradableCard>();
        foreach (var card in catalog)
        {
            if (!inventory.Copies.TryGetValue(card.Key, out var photos))
            {
                continue;
            }

            var effective = photos
                .Where(p => reservedPhotoIds is null || !reservedPhotoIds.Contains(p.PhotoId))
                .OrderBy(Quality)
                .ThenBy(p => p.PhotoId, StringComparer.Ordinal)
                .ToList();
            if (effective.Count < 2)
            {
                continue;
            }

            // effective[^1] is the retained best copy; the worst goes first.
            result.Add(new TradableCard(card, effective.Count - 1, effective[0]));
        }

        return Sort(result, c => c.Card);
    }

    /// <summary>Catalog cards the user owns zero copies of, ordered by series then card number.</summary>
    public static IReadOnlyList<TradeCatalogCard> GetWantedCards(
        TradeInventory inventory, IEnumerable<TradeCatalogCard> catalog) =>
        Sort(catalog.Where(c => inventory.CopyCount(c.Key) == 0).ToList(), c => c);

    private static List<T> Sort<T>(List<T> items, Func<T, TradeCatalogCard> card) => items
        .OrderBy(i => card(i).Series.ToUpperInvariant(), StringComparer.Ordinal)
        .ThenBy(i => card(i).CardNumber.ToUpperInvariant(), StringComparer.Ordinal)
        .ToList();

    /// <summary>Cards <paramref name="giver"/> can offer that <paramref name="receiver"/> does not own.</summary>
    public static IReadOnlyList<TradableCard> GetOfferableTo(
        TradeInventory giver, TradeInventory receiver, IEnumerable<TradeCatalogCard> catalog,
        ISet<string>? giverReservedPhotoIds = null)
    {
        var cards = catalog as IReadOnlyCollection<TradeCatalogCard> ?? catalog.ToList();
        return GetTradableCards(giver, cards, giverReservedPhotoIds)
            .Where(t => receiver.CopyCount(t.Card.Key) == 0)
            .ToList();
    }

    public static TradeSuggestion Suggest(
        TradeInventory mine,
        TradeInventory friend,
        IEnumerable<TradeCatalogCard> catalog,
        ISet<string>? myReservedPhotoIds = null,
        ISet<string>? friendReservedPhotoIds = null)
    {
        var cards = catalog.ToList();
        var give = GetOfferableTo(mine, friend, cards, myReservedPhotoIds);
        var receive = GetOfferableTo(friend, mine, cards, friendReservedPhotoIds);
        return Pair(
            give.Select(ToOffer).ToList(),
            receive.Select(ToOffer).ToList());
    }

    private static TradeCardOffer ToOffer(TradableCard t) =>
        new(t.OfferedPhoto.PhotoId, t.Card.Series, t.Card.CardNumber, t.Card.CardName,
            CardRarity.Normalize(t.Card.Rarity));

    /// <summary>Pairs candidate offers: same tier first, then closest weight, greedy by tier descending.</summary>
    public static TradeSuggestion Pair(IReadOnlyList<TradeCardOffer> give, IReadOnlyList<TradeCardOffer> receive)
    {
        var giveByTier = GroupByTier(give);
        var receiveByTier = GroupByTier(receive);
        var pairs = new List<TradePair>();

        for (var tier = 2; tier >= 0; tier--)
        {
            var count = Math.Min(giveByTier[tier].Count, receiveByTier[tier].Count);
            for (var i = 0; i < count; i++)
            {
                pairs.Add(MakePair(giveByTier[tier][0], receiveByTier[tier][0]));
                giveByTier[tier].RemoveAt(0);
                receiveByTier[tier].RemoveAt(0);
            }
        }

        // Leftovers: per tier only one side has any left. Highest tier first, closest weight.
        var giveLeft = giveByTier.SelectMany(l => l).ToList();
        var receiveLeft = receiveByTier.SelectMany(l => l).ToList();
        while (giveLeft.Count > 0 && receiveLeft.Count > 0)
        {
            var topGive = giveLeft.Max(o => Weight(o.Rarity));
            var topReceive = receiveLeft.Max(o => Weight(o.Rarity));
            if (topGive >= topReceive)
            {
                var g = giveLeft.First(o => Weight(o.Rarity) == topGive);
                var r = Closest(receiveLeft, topGive);
                giveLeft.Remove(g);
                receiveLeft.Remove(r);
                pairs.Add(MakePair(g, r));
            }
            else
            {
                var r = receiveLeft.First(o => Weight(o.Rarity) == topReceive);
                var g = Closest(giveLeft, topReceive);
                receiveLeft.Remove(r);
                giveLeft.Remove(g);
                pairs.Add(MakePair(g, r));
            }
        }

        return Summarize(pairs);
    }

    private static TradeCardOffer Closest(List<TradeCardOffer> pool, int weight) => pool
        .OrderBy(o => Math.Abs(Weight(o.Rarity) - weight))
        .ThenByDescending(o => Weight(o.Rarity))
        .First();

    private static List<TradeCardOffer>[] GroupByTier(IReadOnlyList<TradeCardOffer> offers)
    {
        var groups = new[] { new List<TradeCardOffer>(), new List<TradeCardOffer>(), new List<TradeCardOffer>() };
        foreach (var offer in offers
                     .OrderBy(o => o.Series.ToUpperInvariant(), StringComparer.Ordinal)
                     .ThenBy(o => o.CardNumber.ToUpperInvariant(), StringComparer.Ordinal))
        {
            groups[Tier(offer.Rarity)].Add(offer);
        }

        return groups;
    }

    /// <summary>Builds a pair from a chosen give/receive card, e.g. for manual swaps.</summary>
    public static TradePair MakePair(TradeCardOffer give, TradeCardOffer receive) =>
        new(give, receive, Weight(give.Rarity) - Weight(receive.Rarity), Tier(give.Rarity) != Tier(receive.Rarity));

    /// <summary>Recomputes the balance indicator, e.g. after the user deselects or swaps pairs.</summary>
    public static TradeSuggestion Summarize(IReadOnlyList<TradePair> pairs) => new(
        pairs,
        new TradeBalance(
            pairs.Count,
            pairs.Count,
            pairs.Sum(p => Weight(p.Give.Rarity)),
            pairs.Sum(p => Weight(p.Receive.Rarity)),
            pairs.Count(p => p.Unausgewogen)));
}
