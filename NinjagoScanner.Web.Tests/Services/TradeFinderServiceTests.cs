using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>web-trade-finder page data against the in-process catalog and fake PictureService.</summary>
public class TradeFinderServiceTests
{
    private static TradeFinderService CreateFinder(TradeTestEnv env)
    {
        var loader = new TradeInventoryLoader(env.CatalogClient, env.PictureClient);
        var access = new FriendAccessService(env.Db);
        return new TradeFinderService(
            env.Db, loader, env.PictureClient, access,
            new TradePartnerService(new FriendService(env.Db), access, loader, env.Db));
    }

    [Fact]
    public async Task Session_pairs_my_duplicate_with_the_friends_duplicate_and_flags_unequal_rarity()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var session = await CreateFinder(env).LoadSessionAsync(env.A, "bob");

        Assert.Equal(TradeFinderStatus.Ready, session.Status);
        Assert.Equal("bob", session.PartnerUserName);
        // Bob already owns #4, so only the legendary duplicate is wanted; Bob offers common Jay.
        var pair = Assert.Single(session.Suggestion.Pairs);
        Assert.Contains(pair.Give.PhotoId, new[] { "la1", "la2" });
        Assert.Contains(pair.Receive.PhotoId, new[] { "b6a", "b6b", "b6c" });
        Assert.True(pair.Unausgewogen);
        Assert.False(session.Suggestion.Balance.IsBalanced);
        Assert.Equal(1, session.Suggestion.Balance.UnausgewogenCount);
    }

    [Fact]
    public async Task Session_never_offers_more_than_one_copy_of_a_card_and_never_single_or_unmapped_cards()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var session = await CreateFinder(env).LoadSessionAsync(env.A, "bob");

        Assert.Single(session.GiveCandidates, c => c.CardNumber == "2");
        Assert.DoesNotContain(session.GiveCandidates, c => c.PhotoId == "a5");
        Assert.DoesNotContain(session.ReceiveCandidates, c => c.PhotoId is "b4" or "bx");
        Assert.Single(session.ReceiveCandidates);
    }

    [Fact]
    public async Task Session_provides_card_images_for_offered_cards()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var session = await CreateFinder(env).LoadSessionAsync(env.A, "bob");

        foreach (var offer in session.GiveCandidates.Concat(session.ReceiveCandidates))
        {
            Assert.True(session.ImageUrls.ContainsKey(offer.PhotoId), offer.PhotoId);
        }
    }

    [Fact]
    public async Task Session_for_non_friend_is_unavailable()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var session = await CreateFinder(env).LoadSessionAsync(env.A, "carol");

        Assert.Equal(TradeFinderStatus.Unavailable, session.Status);
        Assert.Empty(session.Suggestion.Pairs);
    }

    [Fact]
    public async Task Session_for_unknown_user_is_unavailable()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var session = await CreateFinder(env).LoadSessionAsync(env.A, "nobody");

        Assert.Equal(TradeFinderStatus.Unavailable, session.Status);
    }

    [Fact]
    public async Task Friend_with_visibility_only_me_is_unavailable_and_listed_as_such()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await new FriendAccessService(env.Db).SetOwnVisibilityAsync(env.B, CollectionVisibility.Private);
        var finder = CreateFinder(env);

        var session = await finder.LoadSessionAsync(env.A, "bob");
        var overview = await finder.LoadOverviewAsync(env.A);

        Assert.Equal(TradeFinderStatus.Unavailable, session.Status);
        var partner = Assert.Single(overview.Partners);
        Assert.False(partner.IsAvailable);
    }

    [Fact]
    public async Task Overview_lists_friend_with_exchange_counts_in_both_directions()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var overview = await CreateFinder(env).LoadOverviewAsync(env.A);

        var partner = Assert.Single(overview.Partners);
        Assert.Equal("bob", partner.UserName);
        Assert.True(partner.IsAvailable);
        Assert.Equal(1, partner.OfferCount);
        Assert.Equal(1, partner.WantCount);
    }

    [Fact]
    public async Task Cards_in_pending_trades_are_excluded_from_the_next_suggestion()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var finder = CreateFinder(env);
        var propose = await env.Service.ProposeAsync(env.A, env.B, ["la1"], ["b6a"]);
        Assert.True(propose.Success, propose.Message);

        var session = await finder.LoadSessionAsync(env.A, "bob");

        Assert.DoesNotContain(session.GiveCandidates, c => c.PhotoId == "la1");
        Assert.DoesNotContain(session.ReceiveCandidates, c => c.PhotoId == "b6a");
        var overview = await finder.LoadOverviewAsync(env.A);
        Assert.Contains("la1", overview.ReservedPhotoIds);
    }

    [Fact]
    public async Task Suggestion_can_be_proposed_directly()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var session = await CreateFinder(env).LoadSessionAsync(env.A, "bob");
        var draft = new TradeProposalDraft(session.Suggestion, session.GiveCandidates, session.ReceiveCandidates);

        var result = await env.Service.ProposeAsync(env.A, session.PartnerUserId, draft.GivePhotoIds, draft.ReceivePhotoIds);

        Assert.True(result.Success, result.Message);
    }
}
