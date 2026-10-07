using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

public class TradeMatchingServiceTests
{
    private const string S = "Season 1";

    internal static TradeCatalogCard Card(string number, string rarity = CardRarity.Common) =>
        new(S, number, "Card " + number, rarity);

    internal static TradeInventory Inv(params (string Number, string[] Photos)[] entries) =>
        new(entries.ToDictionary(
            e => new TradeCardKey(S, e.Number),
            e => (IReadOnlyList<TradeOwnedPhoto>)e.Photos.Select(p => new TradeOwnedPhoto(p)).ToList()));

    internal static TradeInventory InvDetailed(string number, params TradeOwnedPhoto[] photos) =>
        new(new Dictionary<TradeCardKey, IReadOnlyList<TradeOwnedPhoto>>
        {
            [new TradeCardKey(S, number)] = photos
        });

    private static TradeInventory Merge(params TradeInventory[] inventories) =>
        new(inventories.SelectMany(i => i.Copies).ToDictionary(kv => kv.Key, kv => kv.Value));

    // --- Tradable and wanted cards

    [Fact]
    public void Duplicate_is_tradable_with_surplus_copies_minus_one()
    {
        var tradable = TradeMatchingService.GetTradableCards(Inv(("1", ["a", "b", "c"])), [Card("1")]);

        var t = Assert.Single(tradable);
        Assert.Equal(2, t.Surplus);
    }

    [Fact]
    public void Single_copy_is_not_tradable()
    {
        Assert.Empty(TradeMatchingService.GetTradableCards(Inv(("1", ["a"])), [Card("1")]));
    }

    [Fact]
    public void Unmapped_photo_or_card_outside_catalog_is_not_tradable()
    {
        // photos for card 9 never matched the catalog
        Assert.Empty(TradeMatchingService.GetTradableCards(Inv(("9", ["a", "b"])), [Card("1")]));
        Assert.Empty(TradeMatchingService.GetTradableCards(TradeInventory.Empty, [Card("1")]));
    }

    [Fact]
    public void Wanted_is_catalog_card_with_zero_copies()
    {
        var wanted = TradeMatchingService.GetWantedCards(Inv(("1", ["a"])), [Card("1"), Card("2")]);

        Assert.Equal("2", Assert.Single(wanted).CardNumber);
    }

    [Fact]
    public void Reserved_photos_are_excluded_from_offers()
    {
        var inv = Inv(("1", ["a", "b"]));

        Assert.Empty(TradeMatchingService.GetTradableCards(inv, [Card("1")], new HashSet<string> { "a" }));
        Assert.Single(TradeMatchingService.GetTradableCards(Inv(("1", ["a", "b", "c"])), [Card("1")],
            new HashSet<string> { "a" }));
    }

    // --- best copy retention

    [Fact]
    public void Worst_quality_copy_is_offered_and_verified_best_is_kept()
    {
        var inv = InvDetailed("1",
            new TradeOwnedPhoto("verified", ReviewStatuses.Verified, AnalysisStatuses.Ok),
            new TradeOwnedPhoto("uncertain", ReviewStatuses.Unreviewed, AnalysisStatuses.Uncertain),
            new TradeOwnedPhoto("okUnreviewed", ReviewStatuses.Unreviewed, AnalysisStatuses.Ok));

        var t = Assert.Single(TradeMatchingService.GetTradableCards(inv, [Card("1")]));

        Assert.Equal("uncertain", t.OfferedPhoto.PhotoId);
    }

    [Fact]
    public void Unreviewed_is_offered_before_verified_with_two_copies()
    {
        var inv = InvDetailed("1",
            new TradeOwnedPhoto("v", ReviewStatuses.Verified),
            new TradeOwnedPhoto("u", ReviewStatuses.Unreviewed));

        Assert.Equal("u", Assert.Single(TradeMatchingService.GetTradableCards(inv, [Card("1")])).OfferedPhoto.PhotoId);
    }

    // --- Rarity-balanced suggestion

    [Fact]
    public void Equal_tier_pairing_two_common_for_two_common_is_balanced()
    {
        var mine = Merge(Inv(("1", ["m1", "m1b"]), ("2", ["m2", "m2b"])), Inv(("5", ["x"]), ("6", ["x"])));
        var friend = Merge(Inv(("5", ["f5", "f5b"]), ("6", ["f6", "f6b"])), Inv(("1", ["y"]), ("2", ["y"])));
        var catalog = new[] { Card("1"), Card("2"), Card("5"), Card("6") };

        // mine owns 5 and 6 only once -> friend's duplicates of them are wanted? No: I own them, so not wanted.
        // Use inventories where I lack 5 and 6.
        mine = Inv(("1", ["m1", "m1b"]), ("2", ["m2", "m2b"]));
        friend = Merge(Inv(("5", ["f5", "f5b"]), ("6", ["f6", "f6b"])));

        var s = TradeMatchingService.Suggest(mine, friend, catalog);

        Assert.Equal(2, s.Pairs.Count);
        Assert.All(s.Pairs, p => Assert.False(p.Unausgewogen));
        Assert.True(s.Balance.IsBalanced);
        Assert.Equal(0, s.Balance.WeightDifference);
    }

    [Fact]
    public void Limited_for_common_is_flagged_unausgewogen_with_weight_difference()
    {
        var catalog = new[] { Card("1", CardRarity.Limited), Card("2", CardRarity.Common) };
        var mine = Inv(("1", ["m1", "m1b"]));
        var friend = Inv(("2", ["f2", "f2b"]));

        var s = TradeMatchingService.Suggest(mine, friend, catalog);

        var pair = Assert.Single(s.Pairs);
        Assert.True(pair.Unausgewogen);
        Assert.Equal(2, pair.WeightDifference); // 3 - 1
        Assert.False(s.Balance.IsBalanced);
        Assert.Equal(1, s.Balance.UnausgewogenCount);
        Assert.Equal(2, s.Balance.WeightDifference);
    }

    [Fact]
    public void Legendary_for_common_is_flagged()
    {
        var catalog = new[] { Card("1", CardRarity.Legendary), Card("2") };
        var s = TradeMatchingService.Suggest(Inv(("1", ["a", "b"])), Inv(("2", ["c", "d"])), catalog);

        var pair = Assert.Single(s.Pairs);
        Assert.True(pair.Unausgewogen);
        Assert.Equal(8, pair.WeightDifference);
    }

    [Fact]
    public void Suggestion_never_offers_last_copy_nor_more_than_one_copy_of_a_card()
    {
        var catalog = new[] { Card("1"), Card("2"), Card("3") };
        var mine = Inv(("1", ["a", "b"]), ("2", ["c"]));
        var friend = Inv(("3", ["d", "e", "f"]));

        var s = TradeMatchingService.Suggest(mine, friend, catalog);

        var pair = Assert.Single(s.Pairs);
        Assert.Equal("1", pair.Give.CardNumber); // card 2 is a single copy: never offered
        Assert.Single(s.Pairs, p => p.Give.CardNumber == "1");
    }

    [Fact]
    public void Same_tier_is_preferred_over_cross_tier()
    {
        // give: common + limited; receive: limited + common -> pair like with like
        var catalog = new[]
        {
            Card("1"), Card("2", CardRarity.Limited), Card("3", CardRarity.Limited), Card("4")
        };
        var mine = Inv(("1", ["a", "a2"]), ("2", ["b", "b2"]));
        var friend = Inv(("3", ["c", "c2"]), ("4", ["d", "d2"]));

        var s = TradeMatchingService.Suggest(mine, friend, catalog);

        Assert.Equal(2, s.Pairs.Count);
        Assert.All(s.Pairs, p => Assert.False(p.Unausgewogen));
        Assert.Contains(s.Pairs, p => p.Give.CardNumber == "2" && p.Receive.CardNumber == "3");
        Assert.Contains(s.Pairs, p => p.Give.CardNumber == "1" && p.Receive.CardNumber == "4");
    }

    [Fact]
    public void Weight_fallback_matches_closest_weight_highest_tier_first()
    {
        // give: legendary(9), common(1). receive: limited(3), limited(3)
        // legendary leftover pairs with a limited; common pairs with the other limited.
        var give = new[] { Offer("g1", CardRarity.Legendary), Offer("g2", CardRarity.Common) };
        var receive = new[] { Offer("r1", CardRarity.Limited), Offer("r2", CardRarity.Limited) };

        var s = TradeMatchingService.Pair(give, receive);

        Assert.Equal(2, s.Pairs.Count);
        Assert.Equal(2, s.Balance.UnausgewogenCount);
        Assert.Equal(10, s.Balance.GiveWeight);
        Assert.Equal(6, s.Balance.ReceiveWeight);
    }

    [Fact]
    public void Unequal_candidate_counts_still_yield_equal_pair_counts()
    {
        var give = new[] { Offer("g1"), Offer("g2"), Offer("g3") };
        var receive = new[] { Offer("r1") };

        var s = TradeMatchingService.Pair(give, receive);

        Assert.Single(s.Pairs);
        Assert.True(s.Balance.IsEqualCount);
    }

    [Fact]
    public void No_overlap_yields_empty_suggestion()
    {
        var s = TradeMatchingService.Suggest(Inv(("1", ["a", "b"])), Inv(("1", ["c"])), [Card("1")]);

        Assert.Empty(s.Pairs);
        Assert.True(s.Balance.IsBalanced);
    }

    [Fact]
    public void One_sided_overlap_yields_empty_suggestion()
    {
        var catalog = new[] { Card("1"), Card("2") };
        var s = TradeMatchingService.Suggest(Inv(("1", ["a", "b"])), Inv(("2", ["c"])), catalog);

        Assert.Empty(s.Pairs);
    }

    [Fact]
    public void Friend_owning_the_card_already_is_not_offered_it()
    {
        var s = TradeMatchingService.Suggest(
            Inv(("1", ["a", "b"])), Inv(("1", ["c"]), ("2", ["d", "e"])), [Card("1"), Card("2")]);

        Assert.Empty(s.Pairs);
    }

    [Fact]
    public void Unknown_rarity_counts_as_common()
    {
        Assert.Equal(1, TradeMatchingService.Weight("???"));
        Assert.Equal(1, TradeMatchingService.Weight(null));
        Assert.Equal(3, TradeMatchingService.Weight("LIMITED"));
        Assert.Equal(9, TradeMatchingService.Weight("legendary"));
    }

    [Fact]
    public void Suggestion_is_deterministic()
    {
        var catalog = Enumerable.Range(1, 10).Select(i => Card(i.ToString())).ToList();
        var mine = Inv(("1", ["a", "b"]), ("2", ["c", "d"]), ("3", ["e", "f"]));
        var friend = Inv(("7", ["g", "h"]), ("8", ["i", "j"]), ("9", ["k", "l"]));

        var one = TradeMatchingService.Suggest(mine, friend, catalog);
        var two = TradeMatchingService.Suggest(mine, friend, catalog.AsEnumerable().Reverse());

        Assert.Equal(one.Pairs.Select(p => (p.Give.PhotoId, p.Receive.PhotoId)),
            two.Pairs.Select(p => (p.Give.PhotoId, p.Receive.PhotoId)));
    }

    // --- Manual adjustment

    [Fact]
    public void Deselecting_a_pair_recomputes_the_balance()
    {
        var catalog = new[] { Card("1"), Card("2", CardRarity.Limited), Card("3"), Card("4") };
        var s = TradeMatchingService.Suggest(
            Inv(("1", ["a", "b"]), ("2", ["c", "d"])), Inv(("3", ["e", "f"]), ("4", ["g", "h"])), catalog);
        Assert.Equal(2, s.Pairs.Count);
        Assert.False(s.Balance.IsBalanced);

        var unausgewogen = s.Pairs.Where(p => p.Unausgewogen).ToList();
        var remaining = TradeMatchingService.Summarize(s.Pairs.Except(unausgewogen).ToList());

        Assert.True(remaining.Balance.IsBalanced);
        Assert.Equal(remaining.Balance.GiveCount, remaining.Balance.ReceiveCount);
    }

    [Fact]
    public void Manual_swap_pair_is_flagged_by_tier()
    {
        var pair = TradeMatchingService.MakePair(Offer("a", CardRarity.Common), Offer("b", CardRarity.Common));
        var cross = TradeMatchingService.MakePair(Offer("a", CardRarity.Common), Offer("b", CardRarity.Legendary));

        Assert.False(pair.Unausgewogen);
        Assert.True(cross.Unausgewogen);
        Assert.Equal(-8, cross.WeightDifference);
    }

    [Fact]
    public void Empty_summary_is_balanced_with_zero_weights()
    {
        var s = TradeMatchingService.Summarize([]);

        Assert.True(s.Balance.IsBalanced);
        Assert.Equal(0, s.Balance.GiveWeight);
    }

    // --- Randomized invariants

    [Fact]
    public void Randomized_invariants_hold()
    {
        var rng = new Random(20261007);
        string[] rarities = [CardRarity.Common, CardRarity.Limited, CardRarity.Legendary];
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var catalog = Enumerable.Range(1, 25)
                .Select(i => Card(i.ToString(), rarities[rng.Next(3)]))
                .ToList();
            TradeInventory Random(string prefix)
            {
                var d = new Dictionary<TradeCardKey, IReadOnlyList<TradeOwnedPhoto>>();
                foreach (var c in catalog)
                {
                    var n = rng.Next(0, 5);
                    if (n == 0) continue;
                    d[c.Key] = Enumerable.Range(0, n)
                        .Select(k => new TradeOwnedPhoto($"{prefix}-{c.CardNumber}-{k}",
                            rng.Next(3) == 0 ? ReviewStatuses.Verified : ReviewStatuses.Unreviewed,
                            rng.Next(2) == 0 ? AnalysisStatuses.Ok : AnalysisStatuses.Uncertain))
                        .ToList();
                }

                return new TradeInventory(d);
            }

            var mine = Random("m");
            var friend = Random("f");

            var s = TradeMatchingService.Suggest(mine, friend, catalog);

            Assert.True(s.Balance.IsEqualCount);
            Assert.Equal(s.Pairs.Count, s.Balance.GiveCount);
            Assert.Equal(s.Pairs.Count, s.Pairs.Select(p => p.Give.Key).Distinct().Count());
            Assert.Equal(s.Pairs.Count, s.Pairs.Select(p => p.Receive.Key).Distinct().Count());
            Assert.Equal(s.Pairs.Count, s.Pairs.Select(p => p.Give.PhotoId).Distinct().Count());
            Assert.Equal(s.Pairs.Count, s.Pairs.Select(p => p.Receive.PhotoId).Distinct().Count());
            foreach (var p in s.Pairs)
            {
                // never the last copy; receiver does not already own it
                Assert.True(mine.CopyCount(p.Give.Key) >= 2);
                Assert.Equal(0, friend.CopyCount(p.Give.Key));
                Assert.True(friend.CopyCount(p.Receive.Key) >= 2);
                Assert.Equal(0, mine.CopyCount(p.Receive.Key));
                Assert.Contains(mine.Copies[p.Give.Key], x => x.PhotoId == p.Give.PhotoId);
                Assert.Contains(friend.Copies[p.Receive.Key], x => x.PhotoId == p.Receive.PhotoId);
                // the offered copy is never strictly better than a retained one
                var best = mine.Copies[p.Give.Key].Max(x => x.ReviewStatus == ReviewStatuses.Verified ? 3
                    : x.AnalysisStatus == AnalysisStatuses.Ok ? 2 : 1);
                var offered = mine.Copies[p.Give.Key].Single(x => x.PhotoId == p.Give.PhotoId);
                var offeredQ = offered.ReviewStatus == ReviewStatuses.Verified ? 3
                    : offered.AnalysisStatus == AnalysisStatuses.Ok ? 2 : 1;
                Assert.True(offeredQ <= best);
                Assert.Equal(p.Unausgewogen, TradeMatchingService.Weight(p.Give.Rarity) != TradeMatchingService.Weight(p.Receive.Rarity));
            }

            // maximal: pairs == min of candidate counts
            var giveCount = TradeMatchingService.GetOfferableTo(mine, friend, catalog).Count;
            var receiveCount = TradeMatchingService.GetOfferableTo(friend, mine, catalog).Count;
            Assert.Equal(Math.Min(giveCount, receiveCount), s.Pairs.Count);
        }
    }

    [Fact]
    public void Randomized_same_tier_pairs_are_maximized()
    {
        var rng = new Random(7);
        string[] rarities = [CardRarity.Common, CardRarity.Limited, CardRarity.Legendary];
        for (var i = 0; i < 200; i++)
        {
            var give = Enumerable.Range(0, rng.Next(0, 8)).Select(k => Offer("g" + k, rarities[rng.Next(3)])).ToList();
            var receive = Enumerable.Range(0, rng.Next(0, 8)).Select(k => Offer("r" + k, rarities[rng.Next(3)])).ToList();

            var s = TradeMatchingService.Pair(give, receive);

            var expectedSame = rarities.Sum(r => Math.Min(
                give.Count(o => o.Rarity == r), receive.Count(o => o.Rarity == r)));
            Assert.Equal(expectedSame, s.Pairs.Count(p => !p.Unausgewogen));
            Assert.Equal(Math.Min(give.Count, receive.Count), s.Pairs.Count);
        }
    }

    private static TradeCardOffer Offer(string id, string rarity = CardRarity.Common) =>
        new(id, S, id, "Card " + id, rarity);
}
