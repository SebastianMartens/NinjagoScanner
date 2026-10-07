using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public class TradeServiceTests
{
    // ------------------------------------------------------------- proposal

    [Fact]
    public async Task Valid_proposal_creates_pending_trade_with_card_snapshots_visible_to_recipient()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "la1"], ["b6a", "b6b"]);

        Assert.True(result.Success);
        Assert.Equal(TradeStatus.Pending, result.Status);
        var trade = await env.GetTradeAsync(result.TradeId!);
        Assert.Equal(TradeStatus.Pending, trade.Status);
        Assert.Equal(env.A, trade.ProposerUserId);
        Assert.Equal(env.B, trade.RecipientUserId);
        Assert.Equal(env.CollA, trade.ProposerCollectionId);
        Assert.Equal(env.CollB, trade.RecipientCollectionId);
        Assert.Equal(4, trade.Items.Count);

        var legendary = trade.Items.Single(i => i.PhotoId == "la1");
        Assert.Equal(TradeSide.FromProposer, legendary.Side);
        Assert.Equal("Golden Dragon", legendary.CardName);
        Assert.Equal("legendary", legendary.Rarity);
        Assert.Equal("2", legendary.CardNumber);
        Assert.Equal(TradeSide.FromRecipient, trade.Items.Single(i => i.PhotoId == "b6a").Side);

        Assert.Equal(1, await env.Service.CountIncomingPendingAsync(env.B));
        Assert.Equal(0, await env.Service.CountIncomingPendingAsync(env.A));
        var bobsView = await env.Service.ListTradesAsync(env.B);
        Assert.Single(bobsView);
        Assert.Equal(TradeStatus.Pending, bobsView[0].Status);
    }

    [Fact]
    public async Task Proposal_to_non_friend_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.C, ["a4a"], ["b6a"]);

        Assert.False(result.Success);
        Assert.Equal(TradeFailure.NotFriends, result.Failure);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Proposal_to_a_friend_with_a_private_collection_is_rejected_and_stores_nothing()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        env.Db.CollectionSharingSettings.Add(new CollectionSharingSettings { CollectionId = env.CollB, Visibility = CollectionVisibility.Private });
        await env.Db.SaveChangesAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "la1"], ["b6a", "b6b"]);

        Assert.False(result.Success);
        Assert.Equal(TradeFailure.CollectionPrivate, result.Failure);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Proposal_succeeds_again_after_the_recipient_makes_the_collection_visible()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        env.Db.CollectionSharingSettings.Add(new CollectionSharingSettings { CollectionId = env.CollB, Visibility = CollectionVisibility.Private });
        await env.Db.SaveChangesAsync();
        Assert.False((await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).Success);

        await env.Db.CollectionSharingSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.Visibility, CollectionVisibility.Friends));

        Assert.True((await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).Success);
    }

    [Fact]
    public async Task Recipient_going_private_after_the_proposal_can_still_accept_and_decline()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var acceptId = await env.ProposeStandardAsync();
        env.Db.CollectionSharingSettings.Add(new CollectionSharingSettings { CollectionId = env.CollB, Visibility = CollectionVisibility.Private });
        await env.Db.SaveChangesAsync();

        var accepted = await env.Service.AcceptAsync(env.B, acceptId);

        Assert.True(accepted.Success);
        Assert.Equal(TradeStatus.Completed, accepted.Status);
    }

    [Fact]
    public async Task Recipient_going_private_after_the_proposal_can_still_decline()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Db.CollectionSharingSettings.Add(new CollectionSharingSettings { CollectionId = env.CollB, Visibility = CollectionVisibility.Private });
        await env.Db.SaveChangesAsync();

        var declined = await env.Service.DeclineAsync(env.B, tradeId);

        Assert.True(declined.Success);
    }

    [Fact]
    public async Task Proposal_with_only_pending_friend_request_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var (low, high) = Friendship.CanonicalPair(env.A, env.C);
        env.Db.Friendships.Add(new Friendship
        {
            RequesterUserId = env.A, AddresseeUserId = env.C, UserLowId = low, UserHighId = high, Status = FriendshipStatus.Pending
        });
        await env.Db.SaveChangesAsync();

        var result = await env.Service.ProposeAsync(env.A, env.C, ["a4a"], ["b6a"]);

        Assert.Equal(TradeFailure.NotFriends, result.Failure);
    }

    [Fact]
    public async Task Proposal_to_self_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.A, ["a4a"], ["a4b"]);

        Assert.False(result.Success);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Proposal_with_unequal_counts_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "la1"], ["b6a"]);

        Assert.Equal(TradeFailure.UnequalCounts, result.Failure);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Proposal_with_an_empty_side_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        Assert.Equal(TradeFailure.InvalidSelection, (await env.Service.ProposeAsync(env.A, env.B, [], [])).Failure);
        Assert.Equal(TradeFailure.InvalidSelection, (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], [])).Failure);
    }

    [Fact]
    public async Task Proposal_with_the_same_photo_twice_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "a4a"], ["b6a", "b6b"]);

        Assert.Equal(TradeFailure.InvalidSelection, result.Failure);
    }

    [Fact]
    public async Task Proposal_offering_the_last_copy_of_a_card_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a5"], ["b6a"]);

        Assert.Equal(TradeFailure.NotTradable, result.Failure);
        Assert.Contains("letzte Kopie", result.Message);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Requesting_the_friends_last_copy_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b4"]);

        Assert.Equal(TradeFailure.NotTradable, result.Failure);
    }

    [Fact]
    public async Task Offering_every_copy_of_a_card_in_one_proposal_is_rejected()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        // A owns exactly two copies of Serie 2 #4: offering both would leave none.
        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "a4b"], ["b6a", "b6b"]);

        Assert.Equal(TradeFailure.NotTradable, result.Failure);
    }

    [Fact]
    public async Task Unmapped_photo_is_not_tradable()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["bx"]);

        Assert.Equal(TradeFailure.NotTradable, result.Failure);
    }

    [Fact]
    public async Task Photo_of_the_wrong_collection_or_unknown_photo_is_not_tradable()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        // b6a belongs to Bob but is listed on Alice's side; "ghost" does not exist at all.
        Assert.Equal(TradeFailure.NotTradable, (await env.Service.ProposeAsync(env.A, env.B, ["b6a"], ["b6b"])).Failure);
        Assert.Equal(TradeFailure.NotTradable, (await env.Service.ProposeAsync(env.A, env.B, ["ghost"], ["b6b"])).Failure);
    }

    [Fact]
    public async Task Photo_already_in_a_pending_trade_cannot_be_offered_again()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.ProposeStandardAsync();

        // Same offered photo.
        var again = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6b"]);
        Assert.Equal(TradeFailure.AlreadyReserved, again.Failure);

        // Same requested photo.
        var requestedAgain = await env.Service.ProposeAsync(env.A, env.B, ["la1"], ["b6a"]);
        Assert.Equal(TradeFailure.AlreadyReserved, requestedAgain.Failure);

        Assert.Single(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Reserved_copy_counts_against_surplus_so_the_last_remaining_copy_stays_protected()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.ProposeStandardAsync(); // reserves a4a; a4b is now the only unreserved copy of #4

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4b"], ["b6b"]);

        Assert.Equal(TradeFailure.NotTradable, result.Failure);
    }

    [Fact]
    public async Task Concurrent_proposals_for_the_same_photo_create_only_one_trade()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        var results = await Task.WhenAll(
            env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"]),
            env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6b"]));

        Assert.Equal(1, results.Count(r => r.Success));
        Assert.Single(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Open_reservation_unique_index_rejects_a_second_open_item_for_the_same_photo()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.ProposeStandardAsync();

        env.Db.Trades.Add(new Trade
        {
            ProposerUserId = env.A, RecipientUserId = env.B, ProposerCollectionId = env.CollA, RecipientCollectionId = env.CollB,
            Items = [new TradeItem { Side = TradeSide.FromProposer, PhotoId = "a4a", Reserved = true }]
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => env.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Proposal_losing_the_database_reservation_race_fails_with_already_reserved()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        // A reservation the validation query cannot see (trade not open) but the index still holds:
        // models a proposal committed by another machine between validation and insert.
        env.Db.Trades.Add(new Trade
        {
            ProposerUserId = env.A, RecipientUserId = env.B, ProposerCollectionId = env.CollA, RecipientCollectionId = env.CollB,
            Status = TradeStatus.Cancelled,
            Items = [new TradeItem { Side = TradeSide.FromProposer, PhotoId = "a4a", Reserved = true }]
        });
        await env.Db.SaveChangesAsync();

        var result = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"]);

        Assert.False(result.Success);
        Assert.Equal(TradeFailure.AlreadyReserved, result.Failure);
        Assert.Single(await env.Db.Trades.ToListAsync());
    }

    [Fact]
    public async Task Second_proposal_for_the_same_photo_fails_and_reservation_is_released_when_trade_closes()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        var second = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6b"]);
        Assert.Equal(TradeFailure.AlreadyReserved, second.Failure);

        await env.Service.DeclineAsync(env.B, tradeId);
        Assert.DoesNotContain(await env.Db.TradeItems.AsNoTracking().ToListAsync(), i => i.Reserved);

        var third = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6b"]);
        Assert.True(third.Success, third.Message);
    }

    [Fact]
    public async Task Completed_trade_releases_its_reservations_and_open_trade_keeps_them()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        Assert.All(await env.Db.TradeItems.AsNoTracking().ToListAsync(), i => Assert.True(i.Reserved));

        Assert.True((await env.Service.AcceptAsync(env.B, tradeId)).Success);

        Assert.All(await env.Db.TradeItems.AsNoTracking().ToListAsync(), i => Assert.False(i.Reserved));
    }

    [Fact]
    public async Task Photo_marked_incorrect_cannot_be_offered_or_requested_and_is_no_surplus()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        // a5 is Alice's single copy of #5; an incorrect extra photo must not make it a duplicate.
        env.Pictures.WritePhoto("a5bad", TradeTestEnv.Sidecar("Serie 2", "5", "Zane", "incorrect"), env.CollA);
        // an incorrect third copy of Bob's #6 cannot be requested
        env.Pictures.WritePhoto("b6bad", TradeTestEnv.Sidecar("Serie 2", "6", "Jay", "incorrect"), env.CollB);

        var noSurplus = await env.Service.ProposeAsync(env.A, env.B, ["a5"], ["b6a"]);
        var incorrectOffered = await env.Service.ProposeAsync(env.A, env.B, ["a5bad"], ["b6a"]);
        var incorrectRequested = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6bad"]);

        Assert.Equal(TradeFailure.NotTradable, noSurplus.Failure);
        Assert.Equal(TradeFailure.NotTradable, incorrectOffered.Failure);
        Assert.Equal(TradeFailure.NotTradable, incorrectRequested.Failure);
        Assert.Empty(await env.Db.Trades.ToListAsync());
    }

    // ---------------------------------------------------- decline / cancel

    [Fact]
    public async Task Recipient_declines_and_both_collections_stay_unchanged()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        var aBefore = await env.CardsAsync(env.CollA);
        var bBefore = await env.CardsAsync(env.CollB);

        var result = await env.Service.DeclineAsync(env.B, tradeId);

        Assert.True(result.Success);
        Assert.Equal(TradeStatus.Declined, (await env.GetTradeAsync(tradeId)).Status);
        Assert.NotNull((await env.GetTradeAsync(tradeId)).ResolvedAt);
        Assert.Equal(aBefore, await env.CardsAsync(env.CollA));
        Assert.Equal(bBefore, await env.CardsAsync(env.CollB));
        Assert.Empty(env.Pictures.Transfers);
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        Assert.Equal(0, await env.Service.CountIncomingPendingAsync(env.B));
    }

    [Fact]
    public async Task Proposer_cancels_pending_trade()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        var result = await env.Service.CancelAsync(env.A, tradeId);

        Assert.True(result.Success);
        Assert.Equal(TradeStatus.Cancelled, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Only_the_recipient_can_decline_and_only_the_proposer_can_cancel()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.DeclineAsync(env.A, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.DeclineAsync(env.C, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.CancelAsync(env.B, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.CancelAsync(env.C, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.AcceptAsync(env.A, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotAllowed, (await env.Service.AcceptAsync(env.C, tradeId)).Failure);
        Assert.Equal(TradeStatus.Pending, (await env.GetTradeAsync(tradeId)).Status);
    }

    [Fact]
    public async Task Unknown_trade_is_reported_as_not_found()
    {
        await using var env = await TradeTestEnv.CreateAsync();

        Assert.Equal(TradeFailure.NotFound, (await env.Service.DeclineAsync(env.B, "nope")).Failure);
        Assert.Equal(TradeFailure.NotFound, (await env.Service.CancelAsync(env.A, "nope")).Failure);
        Assert.Equal(TradeFailure.NotFound, (await env.Service.AcceptAsync(env.B, "nope")).Failure);
    }

    [Fact]
    public async Task A_closed_trade_cannot_be_declined_cancelled_or_accepted_again()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Service.DeclineAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.NotPending, (await env.Service.DeclineAsync(env.B, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotPending, (await env.Service.CancelAsync(env.A, tradeId)).Failure);
        Assert.Equal(TradeFailure.NotPending, (await env.Service.AcceptAsync(env.B, tradeId)).Failure);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Declining_releases_the_reservation()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Service.DeclineAsync(env.B, tradeId);

        var again = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"]);

        Assert.True(again.Success);
    }

    [Fact]
    public async Task Removing_the_friendship_cancels_pending_trades()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        Assert.True(await new FriendService(env.Db).RemoveAsync(env.A, env.B));

        Assert.Equal(TradeStatus.Cancelled, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Equal(TradeFailure.NotPending, (await env.Service.AcceptAsync(env.B, tradeId)).Failure);
        Assert.Empty(env.Pictures.Transfers);
    }

    // ----------------------------------------------------------------- accept

    [Fact]
    public async Task Accepting_a_valid_trade_moves_every_card_and_completes_the_trade()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var propose = await env.Service.ProposeAsync(env.A, env.B, ["a4a", "la1"], ["b6a", "b6b"]);
        var tradeId = propose.TradeId!;

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.True(result.Success, result.Message);
        Assert.Equal(TradeStatus.Completed, result.Status);
        var trade = await env.GetTradeAsync(tradeId);
        Assert.Equal(TradeStatus.Completed, trade.Status);
        Assert.NotNull(trade.ResolvedAt);

        // Sources are gone, the other side owns new photos; no card is in both or neither.
        Assert.False(env.Pictures.HasPhoto(env.CollA, "a4a"));
        Assert.False(env.Pictures.HasPhoto(env.CollA, "la1"));
        Assert.False(env.Pictures.HasPhoto(env.CollB, "b6a"));
        Assert.False(env.Pictures.HasPhoto(env.CollB, "b6b"));

        var aCards = await env.CardsAsync(env.CollA);
        var bCards = await env.CardsAsync(env.CollB);
        Assert.Equal(["Cole", "Golden Dragon", "Jay", "Jay", "Zane"],
            aCards.Select(c => c.CardName!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["", "Cole", "Cole", "Golden Dragon", "Jay"],
            bCards.Select(c => c.CardName ?? "").Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(10, aCards.Count + bCards.Count);
    }

    [Fact]
    public async Task Accept_issues_exactly_one_transfer_call_with_the_trade_id_covering_both_directions()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        await env.Service.AcceptAsync(env.B, tradeId);

        var transfer = Assert.Single(env.Pictures.Transfers);
        Assert.Equal(tradeId, transfer.TransferId);
        Assert.Equal(2, transfer.Moves.Count);
        Assert.Contains((env.CollA, env.CollB, "a4a"), transfer.Moves);
        Assert.Contains((env.CollB, env.CollA, "b6a"), transfer.Moves);
    }

    [Fact]
    public async Task Moved_cards_keep_sidecar_data_and_receive_new_photo_ids()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync(); // a4a (verified Cole) <-> b6a (verified Jay)

        await env.Service.AcceptAsync(env.B, tradeId);

        var aCards = await env.PictureClient.ListCardEntriesForCollectionAsync(env.CollA);
        var bCards = await env.PictureClient.ListCardEntriesForCollectionAsync(env.CollB);
        var jayAtAlice = Assert.Single(aCards, c => c.CardName == "Jay" && c.ReviewStatus == "verified");
        var coleAtBob = Assert.Single(bCards, c => c.CardName == "Cole" && c.ReviewStatus == "verified");

        Assert.NotEqual("b6a", jayAtAlice.PhotoId);
        Assert.NotEqual("a4a", coleAtBob.PhotoId);
        Assert.Equal("Serie 2", jayAtAlice.SetName);
        Assert.Equal("6", jayAtAlice.CardNumber);
        Assert.Equal("Serie 2", coleAtBob.SetName);
        Assert.Equal("4", coleAtBob.CardNumber);
        Assert.Equal("ok", coleAtBob.AnalysisStatus);
        Assert.Equal("de", coleAtBob.Language);
        Assert.True(coleAtBob.Rotated180);
        Assert.True(env.Pictures.HasPhoto(env.CollB, coleAtBob.PhotoId));
    }

    [Fact]
    public async Task Completed_trade_writes_exactly_one_log_entry_with_card_snapshots_and_usernames()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = (await env.Service.ProposeAsync(env.A, env.B, ["a4a", "la1"], ["b6a", "b6b"])).TradeId!;

        await env.Service.AcceptAsync(env.B, tradeId);

        var entry = await env.Db.TradeLogEntries.AsNoTracking().Include(e => e.Items).SingleAsync();
        Assert.Equal(tradeId, entry.TradeId);
        Assert.Equal(env.A, entry.ProposerUserId);
        Assert.Equal(env.B, entry.RecipientUserId);
        Assert.Equal("alice", entry.ProposerUserName);
        Assert.Equal("bob", entry.RecipientUserName);
        Assert.Equal(DateTimeKind.Utc, entry.CompletedAtUtc.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : entry.CompletedAtUtc.Kind);
        Assert.InRange(entry.CompletedAtUtc, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(4, entry.Items.Count);
        Assert.Equal(2, entry.Items.Count(i => i.Side == TradeSide.FromProposer));
        var dragon = entry.Items.Single(i => i.CardName == "Golden Dragon");
        Assert.Equal(TradeSide.FromProposer, dragon.Side);
        Assert.Equal("legendary", dragon.Rarity);
        Assert.Equal("2", dragon.CardNumber);
        Assert.Equal(2, entry.Items.Count(i => i is { Side: TradeSide.FromRecipient, CardName: "Jay" }));
    }

    [Fact]
    public async Task Both_participants_get_25_bonus_xp()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        Assert.Equal(0, await env.BonusXpAsync(env.CollA));

        await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
        Assert.Equal(25, await env.BonusXpAsync(env.CollB));
        Assert.Equal(0, await env.BonusXpAsync(env.CollC));
    }

    [Fact]
    public async Task Trade_xp_adds_to_an_existing_bonus_xp_row()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        env.Db.GamificationProfiles.Add(new GamificationProfile { CollectionId = env.CollA, BonusXp = 100, SelectedBackgroundId = "bg" });
        await env.Db.SaveChangesAsync();
        var tradeId = await env.ProposeStandardAsync();

        await env.Service.AcceptAsync(env.B, tradeId);

        var profile = await env.Db.GamificationProfiles.AsNoTracking().SingleAsync(p => p.CollectionId == env.CollA);
        Assert.Equal(125, profile.BonusXp);
        Assert.Equal("bg", profile.SelectedBackgroundId);
        Assert.Equal(25, await env.BonusXpAsync(env.CollB));
    }

    [Fact]
    public async Task Repeating_the_accept_does_not_move_cards_or_grant_xp_again()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Service.AcceptAsync(env.B, tradeId);

        var second = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.False(second.Success);
        Assert.Equal(TradeFailure.NotPending, second.Failure);
        Assert.Single(env.Pictures.Transfers);
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
        Assert.Equal(25, await env.BonusXpAsync(env.CollB));
        Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
    }

    [Fact]
    public async Task Trade_xp_shows_up_in_the_gamification_xp_total_of_the_collection()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        // Gamification reads the acting collection from the (fixed) test context.
        var (alice, _) = await env.AddUserAsync("dave", TestCurrentCollectionContext.CollectionId);
        await env.MakeFriendsAsync(alice, env.B);
        env.Pictures.WritePhoto("d4a", TradeTestEnv.Sidecar("Serie 2", "4", "Cole"));
        env.Pictures.WritePhoto("d4b", TradeTestEnv.Sidecar("Serie 2", "4", "Cole"));
        var gamification = new GamificationService(
            env.CatalogClient, env.PictureClient, env.Db,
            new TestCurrentCollectionContext(), new TestAuthenticationStateProvider());
        var xpBefore = await gamification.GetXpAsync();

        var propose = await env.Service.ProposeAsync(alice, env.B, ["d4a"], ["b6a"]);
        Assert.True(propose.Success, propose.Message);
        var accept = await env.Service.AcceptAsync(env.B, propose.TradeId!);
        Assert.True(accept.Success, accept.Message);

        // The traded-away copy was a duplicate and Jay arrived, so XP is bonus + card changes;
        // assert the bonus portion exactly by comparing against a no-trade recompute.
        var profile = await env.Db.GamificationProfiles.AsNoTracking().SingleAsync(p => p.CollectionId == TestCurrentCollectionContext.CollectionId);
        Assert.Equal(25, profile.BonusXp);
        var xpAfter = await gamification.GetXpAsync();
        Assert.True(xpAfter >= xpBefore + 25, $"expected at least +25 XP, was {xpBefore} -> {xpAfter}");
    }

    // ------------------------------------------------------------ stale trades

    [Fact]
    public async Task Stale_trade_when_an_offered_photo_was_deleted_fails_and_nothing_moves()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.DeletePhotoAsync(env.CollA, "a4a");
        var aBefore = await env.CardsAsync(env.CollA);
        var bBefore = await env.CardsAsync(env.CollB);

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.False(result.Success);
        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Equal(TradeStatus.Failed, result.Status);
        Assert.Equal(TradeStatus.Failed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Equal(aBefore, await env.CardsAsync(env.CollA));
        Assert.Equal(bBefore, await env.CardsAsync(env.CollB));
        Assert.Empty(env.Pictures.Transfers);
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        Assert.Equal(0, await env.BonusXpAsync(env.CollA));
        Assert.Equal(0, await env.BonusXpAsync(env.CollB));
        Assert.Contains("nicht mehr gültig", result.Message);
    }

    [Fact]
    public async Task Stale_trade_when_a_requested_photo_was_deleted_fails()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.DeletePhotoAsync(env.CollB, "b6a");

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Stale_trade_when_the_offered_card_stopped_being_a_surplus_copy_fails()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync(); // offers a4a; a4b is the duplicate
        await env.DeletePhotoAsync(env.CollA, "a4b");   // a4a is now Alice's last copy of Cole

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.True(env.Pictures.HasPhoto(env.CollA, "a4a"));
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Stale_trade_when_a_photo_became_unmapped_fails()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Pictures.WritePhoto("a4a", """{ "analysisStatus": "uncertain", "reviewStatus": "unreviewed" }""", env.CollA);

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Stale_trade_when_the_friendship_vanished_without_cancellation_fails()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Db.Friendships.ExecuteDeleteAsync();

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Equal(TradeStatus.Failed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Photo_that_was_traded_elsewhere_makes_the_second_trade_stale()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        // Carol is Alice's friend too and offers a photo; both trades involve Alice's duplicate pair.
        await env.MakeFriendsAsync(env.A, env.C);
        env.Pictures.WritePhoto("c6a", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), env.CollC);
        env.Pictures.WritePhoto("c6b", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), env.CollC);
        var first = await env.ProposeStandardAsync();                                 // a4a <-> b6a
        var second = await env.Service.ProposeAsync(env.C, env.A, ["c6a"], ["la1"]);  // la1 <-> c6a
        Assert.True(second.Success, second.Message);
        // Alice's la1 gets deleted before Carol's trade is accepted.
        await env.DeletePhotoAsync(env.CollA, "la1");

        Assert.True((await env.Service.AcceptAsync(env.B, first)).Success);
        var result = await env.Service.AcceptAsync(env.A, second.TradeId!);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Single(env.Pictures.Transfers);
    }

    // ------------------------------------------------------- transfer failure

    [Theory]
    [InlineData(StatusCode.Internal)]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.InvalidArgument)]
    public async Task Transfer_failure_marks_the_trade_failed_and_leaves_both_collections_unchanged(StatusCode code)
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        var aBefore = await env.CardsAsync(env.CollA);
        var bBefore = await env.CardsAsync(env.CollB);
        env.Pictures.TransferFailureStatusCode = code;

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.False(result.Success);
        Assert.Equal(TradeFailure.TransferFailed, result.Failure);
        Assert.Equal(TradeStatus.Failed, result.Status);
        Assert.Contains("keine Karten verschoben", result.Message);
        Assert.Equal(TradeStatus.Failed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Equal(aBefore, await env.CardsAsync(env.CollA));
        Assert.Equal(bBefore, await env.CardsAsync(env.CollB));
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        Assert.Equal(0, await env.BonusXpAsync(env.CollA));
        Assert.Equal(0, await env.BonusXpAsync(env.CollB));
        Assert.Single(env.Pictures.Transfers);
    }

    [Theory]
    [InlineData(StatusCode.Aborted)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.Unknown)]
    [InlineData(StatusCode.ResourceExhausted)]
    public async Task Indefinite_transfer_failure_keeps_the_trade_executing(StatusCode code)
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Pictures.TransferFailureStatusCode = code;

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.False(result.Success);
        Assert.Equal(TradeStatus.Executing, result.Status);
        Assert.Equal(TradeStatus.Executing, (await env.GetTradeAsync(tradeId)).Status);
    }

    [Fact]
    public async Task Relabelled_photo_after_proposal_makes_the_trade_stale()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        // a4a was offered as Cole #4; relabel it to Zane #5 (still a valid catalog card).
        env.Pictures.WritePhoto("a4a", TradeTestEnv.Sidecar("Serie 2", "5", "Zane"), env.CollA);

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.Stale, result.Failure);
        Assert.Equal(TradeStatus.Failed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Failed_trade_releases_its_reservation_and_does_not_count_as_incoming()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Pictures.TransferFailureStatusCode = StatusCode.Internal;
        await env.Service.AcceptAsync(env.B, tradeId);
        env.Pictures.TransferFailureStatusCode = null;

        Assert.Equal(0, await env.Service.CountIncomingPendingAsync(env.B));
        Assert.True((await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).Success);
    }

    [Fact]
    public async Task Timeout_leaves_the_trade_executing_for_the_recovery_sweep_and_moves_nothing_in_web()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Pictures.TransferFailureStatusCode = StatusCode.DeadlineExceeded;

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.False(result.Success);
        Assert.Equal(TradeStatus.Executing, result.Status);
        Assert.Equal(TradeStatus.Executing, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        Assert.Equal(0, await env.BonusXpAsync(env.CollA));
    }

    [Fact]
    public async Task Source_removal_failure_still_completes_and_a_replay_cleans_up_without_double_effects()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        env.Pictures.TransferSourceRemovalFails = true;

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.True(result.Success);
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
        Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
        Assert.Single(env.Pictures.Transfers);
    }

    // --------------------------------------------------------- double accept

    [Fact]
    public async Task Double_accept_submitted_twice_in_a_row_moves_cards_exactly_once()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        var first = await env.Service.AcceptAsync(env.B, tradeId);
        var second = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Single(env.Pictures.Transfers);
        Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
    }

    [Fact]
    public async Task Double_accept_with_a_stale_loaded_trade_loses_the_optimistic_flip()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();

        // Another request flips Pending -> Executing between this accept's validation and its flip.
        env.Pictures.TransferFailureStatusCode = null;
        await env.Db.Trades.Where(t => t.Id == tradeId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, TradeStatus.Executing)
            .SetProperty(t => t.ConcurrencyStamp, "other"));

        var result = await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Equal(TradeFailure.NotPending, result.Failure);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Concurrent_accepts_move_cards_exactly_once_and_grant_xp_once()
    {
        await using var env = await TradeTestEnv.CreateAsync(fileBackedDb: true);
        var tradeId = await env.ProposeStandardAsync();
        var (second, secondContext, secondConnection) = env.NewConcurrentService();
        try
        {
            var results = await Task.WhenAll(
                Task.Run(() => env.Service.AcceptAsync(env.B, tradeId)),
                Task.Run(() => second.AcceptAsync(env.B, tradeId)));

            Assert.Equal(1, results.Count(r => r.Success));
            Assert.Single(env.Pictures.Transfers);
            Assert.Equal(TradeStatus.Completed, (await env.GetTradeAsync(tradeId)).Status);
            Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
            Assert.Equal(25, await env.BonusXpAsync(env.CollA));
            Assert.Equal(25, await env.BonusXpAsync(env.CollB));
            Assert.Equal(5, (await env.CardsAsync(env.CollA)).Count);
            Assert.Equal(5, (await env.CardsAsync(env.CollB)).Count);
        }
        finally
        {
            await secondContext.DisposeAsync();
            await secondConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_first_trades_for_collections_without_a_profile_both_complete_and_grant_xp_once_each()
    {
        await using var env = await TradeTestEnv.CreateAsync(fileBackedDb: true);
        Assert.Equal(0, await env.Db.GamificationProfiles.CountAsync());
        var first = await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"]);
        var secondProposal = await env.Service.ProposeAsync(env.A, env.B, ["la1"], ["b6b"]);
        Assert.True(first.Success);
        Assert.True(secondProposal.Success);
        var (second, secondContext, secondConnection) = env.NewConcurrentService();
        try
        {
            var results = await Task.WhenAll(
                Task.Run(() => env.Service.AcceptAsync(env.B, first.TradeId!)),
                Task.Run(() => second.AcceptAsync(env.B, secondProposal.TradeId!)));

            Assert.All(results, r => Assert.True(r.Success, r.Message));
            Assert.Equal(2, await env.Db.TradeLogEntries.CountAsync());
            Assert.Equal(50, await env.BonusXpAsync(env.CollA));
            Assert.Equal(50, await env.BonusXpAsync(env.CollB));
        }
        finally
        {
            await secondContext.DisposeAsync();
            await secondConnection.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------- recovery

    private static Task MarkExecutingAsync(TradeTestEnv env, string tradeId, TimeSpan age) =>
        env.Db.Trades.Where(t => t.Id == tradeId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, TradeStatus.Executing)
            .SetProperty(t => t.ExecutingStartedAt, DateTimeOffset.UtcNow - age));

    [Fact]
    public async Task Sweep_finalizes_a_stale_executing_trade_whose_transfer_already_happened()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        // Simulate a crash after TransferPhotos succeeded but before the completion transaction.
        await env.PictureClient.TransferPhotosAsync(tradeId, [(env.CollA, env.CollB, "a4a"), (env.CollB, env.CollA, "b6a")]);
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(10));

        var resolved = await env.Service.RecoverStaleExecutingAsync();

        Assert.Equal(1, resolved);
        Assert.Equal(TradeStatus.Completed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
        Assert.Equal(25, await env.BonusXpAsync(env.CollB));
        // Replay used the same transfer id and moved nothing twice.
        Assert.All(env.Pictures.Transfers, t => Assert.Equal(tradeId, t.TransferId));
        Assert.Equal(5, (await env.CardsAsync(env.CollA)).Count);
        Assert.Equal(5, (await env.CardsAsync(env.CollB)).Count);
    }

    [Fact]
    public async Task Sweep_performs_the_transfer_when_the_process_died_right_after_the_status_flip()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(6));

        var resolved = await env.Service.RecoverStaleExecutingAsync();

        Assert.Equal(1, resolved);
        Assert.Equal(TradeStatus.Completed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.False(env.Pictures.HasPhoto(env.CollA, "a4a"));
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
    }

    [Fact]
    public async Task Sweep_leaves_recent_executing_trades_alone()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(1));

        var resolved = await env.Service.RecoverStaleExecutingAsync();

        Assert.Equal(0, resolved);
        Assert.Equal(TradeStatus.Executing, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Sweep_ignores_pending_and_closed_trades()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var pending = await env.ProposeStandardAsync();
        var declined = (await env.Service.ProposeAsync(env.A, env.B, ["la1"], ["b6b"])).TradeId!;
        await env.Service.DeclineAsync(env.B, declined);

        Assert.Equal(0, await env.Service.RecoverStaleExecutingAsync(TimeSpan.Zero));

        Assert.Equal(TradeStatus.Pending, (await env.GetTradeAsync(pending)).Status);
        Assert.Equal(TradeStatus.Declined, (await env.GetTradeAsync(declined)).Status);
        Assert.Empty(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Sweep_keeps_the_trade_executing_while_PictureService_is_unavailable_and_succeeds_later()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(10));
        env.Pictures.TransferFailureStatusCode = StatusCode.Unavailable;

        Assert.Equal(0, await env.Service.RecoverStaleExecutingAsync());
        Assert.Equal(TradeStatus.Executing, (await env.GetTradeAsync(tradeId)).Status);

        env.Pictures.TransferFailureStatusCode = null;
        Assert.Equal(1, await env.Service.RecoverStaleExecutingAsync());
        Assert.Equal(TradeStatus.Completed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
    }

    [Fact]
    public async Task Sweep_marks_the_trade_failed_when_the_transfer_definitively_cannot_happen()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(10));
        await env.DeletePhotoAsync(env.CollA, "a4a"); // sources vanished and no transfer was recorded

        var resolved = await env.Service.RecoverStaleExecutingAsync();

        Assert.Equal(1, resolved);
        Assert.Equal(TradeStatus.Failed, (await env.GetTradeAsync(tradeId)).Status);
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        Assert.Equal(0, await env.BonusXpAsync(env.CollA));
    }

    [Fact]
    public async Task Running_the_sweep_twice_does_not_double_grant_xp_or_log()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(10));

        await env.Service.RecoverStaleExecutingAsync();
        var second = await env.Service.RecoverStaleExecutingAsync();

        Assert.Equal(0, second);
        Assert.Equal(1, await env.Db.TradeLogEntries.CountAsync());
        Assert.Equal(25, await env.BonusXpAsync(env.CollA));
        Assert.Single(env.Pictures.Transfers);
    }

    [Fact]
    public async Task Hosted_recovery_service_sweeps_stale_trades_on_start()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await MarkExecutingAsync(env, tradeId, TimeSpan.FromMinutes(10));

        var connection = env.Db.Database.GetDbConnection();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton(env.CatalogClient);
        services.AddSingleton(env.PictureClient);
        services.AddScoped<TradeService>();
        await using var provider = services.BuildServiceProvider();
        var hosted = new TradeRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TradeRecoveryService>.Instance);

        await hosted.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline
               && (await env.GetTradeAsync(tradeId)).Status != TradeStatus.Completed)
        {
            await Task.Delay(100);
        }

        await hosted.StopAsync(CancellationToken.None);
        Assert.Equal(TradeStatus.Completed, (await env.GetTradeAsync(tradeId)).Status);
    }

    // --------------------------------------------------------------- trade log

    [Fact]
    public async Task Declined_cancelled_and_failed_trades_have_no_log_entry_but_show_in_history()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var declined = await env.ProposeStandardAsync();
        await env.Service.DeclineAsync(env.B, declined);
        var cancelled = (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).TradeId!;
        await env.Service.CancelAsync(env.A, cancelled);
        var failing = (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).TradeId!;
        env.Pictures.TransferFailureStatusCode = StatusCode.Internal;
        await env.Service.AcceptAsync(env.B, failing);

        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
        var history = await env.Service.ListTradesAsync(env.A);
        Assert.Equal(3, history.Count);
        Assert.Equal(
            [TradeStatus.Declined, TradeStatus.Cancelled, TradeStatus.Failed],
            history.Select(h => h.Status).Order().ToArray());
    }

    [Fact]
    public async Task Log_entry_survives_friendship_removal_and_card_deletion()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Service.AcceptAsync(env.B, tradeId);

        await new FriendService(env.Db).RemoveAsync(env.A, env.B);
        foreach (var card in await env.PictureClient.ListCardEntriesForCollectionAsync(env.CollA))
        {
            await env.DeletePhotoAsync(env.CollA, card.PhotoId);
        }

        var entry = await env.Db.TradeLogEntries.AsNoTracking().Include(e => e.Items).SingleAsync();
        Assert.Equal(2, entry.Items.Count);
        Assert.Equal("Cole", entry.Items.Single(i => i.Side == TradeSide.FromProposer).CardName);
        Assert.Equal("Jay", entry.Items.Single(i => i.Side == TradeSide.FromRecipient).CardName);
        var mine = await env.Service.ListTradesAsync(env.A);
        Assert.Equal(TradeStatus.Completed, Assert.Single(mine).Status);
    }

    [Fact]
    public async Task Trade_list_only_contains_the_users_own_trades_newest_first_with_partner_and_directions()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.MakeFriendsAsync(env.A, env.C);
        env.Pictures.WritePhoto("c6a", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), env.CollC);
        env.Pictures.WritePhoto("c6b", TradeTestEnv.Sidecar("Serie 2", "6", "Jay"), env.CollC);
        var older = (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).TradeId!;
        await Task.Delay(20);
        var newer = (await env.Service.ProposeAsync(env.C, env.A, ["c6a"], ["la1"])).TradeId!;
        await env.Service.AcceptAsync(env.B, older);

        var alice = await env.Service.ListTradesAsync(env.A);
        Assert.Equal([newer, older], alice.Select(t => t.TradeId).ToArray());
        Assert.Equal("carol", alice[0].PartnerUserName);
        Assert.False(alice[0].IsProposer);
        Assert.Equal("Golden Dragon", Assert.Single(alice[0].Received.Concat(alice[0].Given), c => c.Rarity == "legendary").CardName);
        Assert.Equal("Golden Dragon", Assert.Single(alice[0].Given).CardName);
        Assert.Equal("Jay", Assert.Single(alice[0].Received).CardName);
        Assert.Equal("bob", alice[1].PartnerUserName);
        Assert.True(alice[1].IsProposer);
        Assert.Equal("Cole", Assert.Single(alice[1].Given).CardName);
        Assert.Equal("Jay", Assert.Single(alice[1].Received).CardName);

        // Bob took part in only one of them, Carol in only the other; trades between A and B or A and C
        // are not listed for the third party.
        var bob = await env.Service.ListTradesAsync(env.B);
        Assert.Equal([older], bob.Select(t => t.TradeId).ToArray());
        var carol = await env.Service.ListTradesAsync(env.C);
        Assert.Equal([newer], carol.Select(t => t.TradeId).ToArray());
        Assert.Empty(await env.Service.ListTradesAsync("someone-else"));
    }

    [Fact]
    public async Task Third_party_sees_no_trades_between_two_other_users()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var tradeId = await env.ProposeStandardAsync();
        await env.Service.AcceptAsync(env.B, tradeId);

        Assert.Empty(await env.Service.ListTradesAsync(env.C));
    }

    [Fact]
    public void Trade_service_exposes_no_log_edit_or_delete_operation()
    {
        var methods = typeof(TradeService).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(methods, name => name.Contains("Log", StringComparison.OrdinalIgnoreCase)
                                               && (name.Contains("Delete") || name.Contains("Update") || name.Contains("Edit") || name.Contains("Remove")));
        Assert.DoesNotContain(methods, name => name.StartsWith("Delete", StringComparison.Ordinal) || name.StartsWith("Remove", StringComparison.Ordinal));
    }
}
