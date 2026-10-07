using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;
using static NinjagoScanner.Web.Tests.Services.TradeMatchingServiceTests;

namespace NinjagoScanner.Web.Tests.Services;

public class TradePartnerServiceTests
{
    private sealed class FakeLoader(Dictionary<string, TradeInventory> inventories) : ITradeInventoryLoader
    {
        public List<string> Loaded { get; } = [];

        public Task<TradeInventory> LoadAsync(string collectionId, CancellationToken cancellationToken = default)
        {
            Loaded.Add(collectionId);
            return Task.FromResult(inventories[collectionId]);
        }
    }

    private static async Task MakeFriendsAsync(TestAppDb db, string a, string b)
    {
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);
    }

    private static readonly TradeCatalogCard[] Catalog =
        Enumerable.Range(1, 10).Select(i => Card(i.ToString())).ToArray();

    // I own duplicates of 1..5 and nothing else.
    private static TradeInventory Mine() =>
        Inv(("1", ["a1", "b1"]), ("2", ["a2", "b2"]), ("3", ["a3", "b3"]), ("4", ["a4", "b4"]), ("5", ["a5", "b5"]));

    private static TradePartnerService Service(TestAppDb db, FakeLoader loader) =>
        new(new FriendService(db.DbContext), new FriendAccessService(db.DbContext), loader);

    [Fact]
    public async Task Friend_with_more_swappable_cards_is_ranked_first()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (x, cx) = await db.AddUserAsync("X");
        var (y, cy) = await db.AddUserAsync("Y");
        await MakeFriendsAsync(db, me, x);
        await MakeFriendsAsync(db, me, y);
        var loader = new FakeLoader(new()
        {
            // Y: 2 swappable (wants 1,2 ; offers 6,7)
            [cy] = Inv(("3", ["y3"]), ("4", ["y4"]), ("5", ["y5"]), ("6", ["y6", "y6b"]), ("7", ["y7", "y7b"])),
            // X: 5 swappable
            [cx] = Inv(("6", ["x6", "x6b"]), ("7", ["x7", "x7b"]), ("8", ["x8", "x8b"]), ("9", ["x9", "x9b"]),
                ("10", ["x10", "x10b"]))
        });

        var ranks = await Service(db, loader).RankPartnersAsync(me, Mine(), Catalog);

        Assert.Equal(["X", "Y"], ranks.Select(r => r.UserName));
        Assert.Equal(5, ranks[0].ExchangeableCount);
        Assert.Equal(5, ranks[0].OfferCount);
        Assert.Equal(5, ranks[0].WantCount);
        Assert.Equal(2, ranks[1].ExchangeableCount);
        Assert.Equal(2, ranks[1].WantCount);
        Assert.Equal(2, ranks[1].OfferCount); // I can offer 1 and 2 (friend owns 3,4,5 already)
    }

    [Fact]
    public async Task Counts_are_reported_per_direction()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (x, cx) = await db.AddUserAsync("X");
        await MakeFriendsAsync(db, me, x);
        var loader = new FakeLoader(new() { [cx] = Inv(("6", ["x6", "x6b"])) });

        var rank = Assert.Single(await Service(db, loader).RankPartnersAsync(me, Mine(), Catalog));

        Assert.Equal(5, rank.OfferCount);
        Assert.Equal(1, rank.WantCount);
        Assert.Equal(1, rank.ExchangeableCount);
    }

    [Fact]
    public async Task Friend_without_overlap_is_listed_last_with_zero_counts()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (a, ca) = await db.AddUserAsync("Aaron"); // alphabetically first, but no overlap
        var (z, cz) = await db.AddUserAsync("Zed");
        await MakeFriendsAsync(db, me, a);
        await MakeFriendsAsync(db, me, z);
        var loader = new FakeLoader(new()
        {
            [ca] = Mine(), // owns everything I'd offer, has nothing extra
            [cz] = Inv(("6", ["z6", "z6b"]))
        });

        var ranks = await Service(db, loader).RankPartnersAsync(me, Mine(), Catalog);

        Assert.Equal(["Zed", "Aaron"], ranks.Select(r => r.UserName));
        Assert.True(ranks[1].IsAvailable);
        Assert.Equal(0, ranks[1].OfferCount);
        Assert.Equal(0, ranks[1].WantCount);
        Assert.Equal(0, ranks[1].ExchangeableCount);
    }

    [Fact]
    public async Task Private_friend_is_unavailable_listed_last_and_never_loaded()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (p, cp) = await db.AddUserAsync("Priv");
        var (z, cz) = await db.AddUserAsync("Zed");
        await MakeFriendsAsync(db, me, p);
        await MakeFriendsAsync(db, me, z);
        await new FriendAccessService(db.DbContext).SetOwnVisibilityAsync(p, CollectionVisibility.Private);
        var loader = new FakeLoader(new() { [cz] = Inv(("6", ["z6", "z6b"])), [cp] = Inv(("6", ["p6", "p6b"])) });

        var ranks = await Service(db, loader).RankPartnersAsync(me, Mine(), Catalog);

        Assert.Equal(["Zed", "Priv"], ranks.Select(r => r.UserName));
        Assert.False(ranks[1].IsAvailable);
        Assert.Equal(0, ranks[1].ExchangeableCount);
        Assert.DoesNotContain(cp, loader.Loaded);
    }

    [Fact]
    public async Task Non_friends_and_pending_requests_are_not_ranked()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (stranger, cs) = await db.AddUserAsync("Stranger");
        var (pending, cp) = await db.AddUserAsync("Pending");
        await new FriendService(db.DbContext).SendRequestAsync(me, pending);
        var loader = new FakeLoader(new() { [cs] = Inv(), [cp] = Inv() });

        var ranks = await Service(db, loader).RankPartnersAsync(me, Mine(), Catalog);

        Assert.Empty(ranks);
        Assert.Empty(loader.Loaded);
        Assert.NotNull(stranger);
    }

    [Fact]
    public async Task No_friends_yields_empty_ranking()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");

        Assert.Empty(await Service(db, new FakeLoader([])).RankPartnersAsync(me, Mine(), Catalog));
    }

    [Fact]
    public async Task My_reserved_photos_reduce_my_offer_count()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Me");
        var (x, cx) = await db.AddUserAsync("X");
        await MakeFriendsAsync(db, me, x);
        var loader = new FakeLoader(new() { [cx] = Inv(("6", ["x6", "x6b"])) });

        var rank = Assert.Single(await Service(db, loader)
            .RankPartnersAsync(me, Mine(), Catalog, new HashSet<string> { "a1", "a2" }));

        Assert.Equal(3, rank.OfferCount);
    }

    [Fact]
    public void Order_ties_break_by_total_overlap_then_name()
    {
        var ordered = TradePartnerService.Order(
        [
            new TradePartnerRank("1", "Bob", 2, 2, 2, true),
            new TradePartnerRank("2", "Amy", 2, 2, 2, true),
            new TradePartnerRank("3", "Cat", 5, 2, 2, true),
            new TradePartnerRank("4", "Dan", 0, 0, 0, false),
            new TradePartnerRank("5", "Eve", 9, 9, 9, true)
        ]);

        Assert.Equal(["Eve", "Cat", "Amy", "Bob", "Dan"], ordered.Select(r => r.UserName));
    }
}
