using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public class FriendServiceTests
{
    // --- Migration (task 2.2) ---

    [Fact]
    public async Task Migrations_apply_to_fresh_database_and_create_new_tables()
    {
        await using var db = await TestAppDb.CreateAsync();

        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
        Assert.Empty(await db.DbContext.CollectionSharingSettings.ToListAsync());
        Assert.Empty(await db.DbContext.Trades.ToListAsync());
        Assert.Empty(await db.DbContext.TradeItems.ToListAsync());
        Assert.Empty(await db.DbContext.TradeLogEntries.ToListAsync());
        Assert.Empty(await db.DbContext.TradeLogItems.ToListAsync());
        Assert.Empty(await db.DbContext.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Unordered_pair_unique_index_rejects_reverse_duplicate_row()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("a");
        var (b, _) = await db.AddUserAsync("b");
        var (low, high) = Friendship.CanonicalPair(a, b);
        db.DbContext.Friendships.Add(new Friendship { RequesterUserId = a, AddresseeUserId = b, UserLowId = low, UserHighId = high });
        await db.DbContext.SaveChangesAsync();

        db.DbContext.Friendships.Add(new Friendship { RequesterUserId = b, AddresseeUserId = a, UserLowId = low, UserHighId = high });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Trade_concurrency_token_rejects_stale_update()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, ca) = await db.AddUserAsync("a");
        var (b, cb) = await db.AddUserAsync("b");
        var trade = new Trade { ProposerUserId = a, RecipientUserId = b, ProposerCollectionId = ca, RecipientCollectionId = cb };
        db.DbContext.Trades.Add(trade);
        await db.DbContext.SaveChangesAsync();

        // Simulate a second writer flipping the status first.
        await db.DbContext.Database.ExecuteSqlRawAsync("UPDATE Trades SET ConcurrencyStamp = 'other'");
        trade.Status = TradeStatus.Executing;
        trade.ConcurrencyStamp = Guid.NewGuid().ToString("n");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.DbContext.SaveChangesAsync());
    }

    // --- Search ---

    [Fact]
    public async Task Search_finds_user_case_insensitive_substring_with_relationship_and_no_email()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Kai");
        await db.AddUserAsync("Lloyd77");
        var service = new FriendService(db.DbContext);

        var results = await service.SearchAsync(me, "lloyd");

        var hit = Assert.Single(results);
        Assert.Equal("Lloyd77", hit.UserName);
        Assert.Equal(FriendRelationship.None, hit.Relationship);
        Assert.DoesNotContain(typeof(FriendSearchResult).GetProperties(), p => p.Name.Contains("Email"));

        Assert.Single(await service.SearchAsync(me, "OYD7"));
    }

    [Fact]
    public async Task Search_excludes_own_account()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Kai");
        await db.AddUserAsync("Kaito");
        var service = new FriendService(db.DbContext);

        var results = await service.SearchAsync(me, "kai");

        Assert.DoesNotContain(results, r => r.UserId == me);
        Assert.Equal(["Kaito"], results.Select(r => r.UserName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(" a ")]
    public async Task Search_with_fewer_than_two_characters_returns_nothing(string? query)
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("Kai");
        await db.AddUserAsync("Anna");
        var service = new FriendService(db.DbContext);

        Assert.Empty(await service.SearchAsync(me, query));
    }

    [Fact]
    public async Task Search_reports_each_relationship_state()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("me");
        var (sent, _) = await db.AddUserAsync("zz-sent");
        var (received, _) = await db.AddUserAsync("zz-received");
        var (friend, _) = await db.AddUserAsync("zz-friend");
        await db.AddUserAsync("zz-none");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(me, sent);
        await service.SendRequestAsync(received, me);
        await service.SendRequestAsync(me, friend);
        await service.SendRequestAsync(friend, me); // mutual -> friends

        var results = (await service.SearchAsync(me, "zz-")).ToDictionary(r => r.UserName, r => r.Relationship);

        Assert.Equal(FriendRelationship.RequestSent, results["zz-sent"]);
        Assert.Equal(FriendRelationship.RequestReceived, results["zz-received"]);
        Assert.Equal(FriendRelationship.Friends, results["zz-friend"]);
        Assert.Equal(FriendRelationship.None, results["zz-none"]);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("me");
        await db.AddUserAsync("a_b-user");
        await db.AddUserAsync("axb-user");
        var service = new FriendService(db.DbContext);

        var results = await service.SearchAsync(me, "a_b");

        Assert.Equal(["a_b-user"], results.Select(r => r.UserName));
    }

    // --- Request lifecycle ---

    [Fact]
    public async Task Send_and_accept_makes_both_friends()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);

        Assert.Equal(SendFriendRequestOutcome.Sent, await service.SendRequestAsync(a, b));
        Assert.False(await service.AreFriendsAsync(a, b));
        var friendshipId = Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId;

        Assert.True(await service.AcceptAsync(b, friendshipId));

        Assert.True(await service.AreFriendsAsync(a, b));
        Assert.True(await service.AreFriendsAsync(b, a));
        Assert.Equal("B", Assert.Single((await service.ListAsync(a)).Friends).UserName);
        Assert.Equal("A", Assert.Single((await service.ListAsync(b)).Friends).UserName);
        Assert.Empty((await service.ListAsync(b)).Incoming);
        Assert.Empty((await service.ListAsync(a)).Outgoing);
    }

    [Fact]
    public async Task Only_addressee_can_accept()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var (c, _) = await db.AddUserAsync("C");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        var id = Assert.Single((await service.ListAsync(a)).Outgoing).FriendshipId;

        Assert.False(await service.AcceptAsync(a, id)); // requester
        Assert.False(await service.AcceptAsync(c, id)); // third party
        Assert.False(await service.AreFriendsAsync(a, b));
    }

    [Fact]
    public async Task Accepting_unknown_or_already_accepted_request_fails()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        var id = Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId;

        Assert.False(await service.AcceptAsync(b, "nope"));
        Assert.True(await service.AcceptAsync(b, id));
        Assert.False(await service.AcceptAsync(b, id));
    }

    [Fact]
    public async Task Duplicate_request_in_same_direction_is_rejected_without_second_record()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);

        Assert.Equal(SendFriendRequestOutcome.AlreadyExists, await service.SendRequestAsync(a, b));
        Assert.Equal(1, await db.DbContext.Friendships.CountAsync());
    }

    [Fact]
    public async Task Request_when_friendship_exists_is_rejected_in_either_direction()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);

        Assert.Equal(SendFriendRequestOutcome.AlreadyExists, await service.SendRequestAsync(a, b));
        Assert.Equal(SendFriendRequestOutcome.AlreadyExists, await service.SendRequestAsync(b, a));
        Assert.Equal(1, await db.DbContext.Friendships.CountAsync());
    }

    [Fact]
    public async Task Mutual_pending_requests_resolve_to_friendship()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);

        Assert.Equal(SendFriendRequestOutcome.BecameFriends, await service.SendRequestAsync(b, a));

        Assert.True(await service.AreFriendsAsync(a, b));
        Assert.Equal(1, await db.DbContext.Friendships.CountAsync());
        Assert.Empty((await service.ListAsync(a)).Outgoing);
        Assert.Empty((await service.ListAsync(b)).Incoming);
    }

    [Fact]
    public async Task Cannot_befriend_self()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var service = new FriendService(db.DbContext);

        Assert.Equal(SendFriendRequestOutcome.CannotBefriendSelf, await service.SendRequestAsync(a, a));
        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
    }

    [Fact]
    public async Task Request_to_unknown_user_is_rejected()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var service = new FriendService(db.DbContext);

        Assert.Equal(SendFriendRequestOutcome.UserNotFound, await service.SendRequestAsync(a, "ghost"));
        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
    }

    [Fact]
    public async Task Decline_removes_request_and_allows_new_request_later()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        var id = Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId;

        Assert.False(await service.DeclineAsync(a, id)); // requester cannot decline
        Assert.True(await service.DeclineAsync(b, id));

        Assert.False(await service.AreFriendsAsync(a, b));
        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
        Assert.Equal(SendFriendRequestOutcome.Sent, await service.SendRequestAsync(a, b));
    }

    [Fact]
    public async Task Cancel_removes_pending_request_only_for_requester()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        var id = Assert.Single((await service.ListAsync(a)).Outgoing).FriendshipId;

        Assert.False(await service.CancelAsync(b, id));
        Assert.True(await service.CancelAsync(a, id));

        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
        Assert.False(await service.CancelAsync(a, id));
    }

    [Fact]
    public async Task Cancel_and_decline_do_not_touch_accepted_friendships()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        var id = Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId;
        await service.AcceptAsync(b, id);

        Assert.False(await service.CancelAsync(a, id));
        Assert.False(await service.DeclineAsync(b, id));
        Assert.True(await service.AreFriendsAsync(a, b));
    }

    // --- Remove ---

    [Fact]
    public async Task Either_friend_can_remove_the_friendship()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);

        Assert.True(await service.RemoveAsync(b, a)); // the addressee removes

        Assert.False(await service.AreFriendsAsync(a, b));
        Assert.Empty((await service.ListAsync(a)).Friends);
        Assert.False(await service.RemoveAsync(a, b)); // nothing left
    }

    [Fact]
    public async Task Remove_does_not_remove_pending_requests_or_unrelated_users()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, _) = await db.AddUserAsync("A");
        var (b, _) = await db.AddUserAsync("B");
        var (c, _) = await db.AddUserAsync("C");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);

        Assert.False(await service.RemoveAsync(a, b));   // pending is cancel, not remove
        Assert.False(await service.RemoveAsync(c, a));   // unrelated
        Assert.Equal(1, await db.DbContext.Friendships.CountAsync());
    }

    [Fact]
    public async Task Remove_cancels_open_trades_between_the_pair_and_keeps_log_and_other_trades()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, ca) = await db.AddUserAsync("A");
        var (b, cb) = await db.AddUserAsync("B");
        var (c, cc) = await db.AddUserAsync("C");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);

        var openAb = new Trade { ProposerUserId = a, RecipientUserId = b, ProposerCollectionId = ca, RecipientCollectionId = cb };
        var openBa = new Trade { ProposerUserId = b, RecipientUserId = a, ProposerCollectionId = cb, RecipientCollectionId = ca };
        var completed = new Trade { ProposerUserId = a, RecipientUserId = b, ProposerCollectionId = ca, RecipientCollectionId = cb, Status = TradeStatus.Completed };
        var otherPair = new Trade { ProposerUserId = a, RecipientUserId = c, ProposerCollectionId = ca, RecipientCollectionId = cc };
        db.DbContext.Trades.AddRange(openAb, openBa, completed, otherPair);
        db.DbContext.TradeLogEntries.Add(new TradeLogEntry { TradeId = completed.Id, CompletedAtUtc = DateTime.UtcNow });
        await db.DbContext.SaveChangesAsync();

        await service.RemoveAsync(a, b);

        db.DbContext.ChangeTracker.Clear();
        var trades = await db.DbContext.Trades.ToDictionaryAsync(t => t.Id);
        Assert.Equal(TradeStatus.Cancelled, trades[openAb.Id].Status);
        Assert.NotNull(trades[openAb.Id].ResolvedAt);
        Assert.Equal(TradeStatus.Cancelled, trades[openBa.Id].Status);
        Assert.Equal(TradeStatus.Completed, trades[completed.Id].Status);
        Assert.Equal(TradeStatus.Pending, trades[otherPair.Id].Status);
        Assert.Equal(1, await db.DbContext.TradeLogEntries.CountAsync());
    }

    [Fact]
    public async Task Remove_leaves_an_executing_trade_to_finish_and_releases_cancelled_reservations()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (a, ca) = await db.AddUserAsync("A");
        var (b, cb) = await db.AddUserAsync("B");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(a, b);
        await service.AcceptAsync(b, Assert.Single((await service.ListAsync(b)).Incoming).FriendshipId);

        var pending = new Trade { ProposerUserId = a, RecipientUserId = b, ProposerCollectionId = ca, RecipientCollectionId = cb };
        pending.Items.Add(new TradeItem { Side = TradeSide.FromProposer, PhotoId = "p1", Reserved = true });
        var executing = new Trade { ProposerUserId = a, RecipientUserId = b, ProposerCollectionId = ca, RecipientCollectionId = cb, Status = TradeStatus.Executing };
        executing.Items.Add(new TradeItem { Side = TradeSide.FromProposer, PhotoId = "p2", Reserved = true });
        db.DbContext.Trades.AddRange(pending, executing);
        await db.DbContext.SaveChangesAsync();

        Assert.True(await service.RemoveAsync(a, b));

        db.DbContext.ChangeTracker.Clear();
        Assert.Equal(TradeStatus.Cancelled, (await db.DbContext.Trades.FindAsync(pending.Id))!.Status);
        Assert.Equal(TradeStatus.Executing, (await db.DbContext.Trades.FindAsync(executing.Id))!.Status);
        var items = await db.DbContext.TradeItems.ToDictionaryAsync(i => i.PhotoId);
        Assert.False(items["p1"].Reserved);
        Assert.True(items["p2"].Reserved);
        Assert.Empty(await db.DbContext.Friendships.ToListAsync());
    }

    [Fact]
    public async Task Remove_racing_with_a_concurrent_accept_never_throws_and_leaves_no_pending_trade()
    {
        for (var round = 0; round < 5; round++)
        {
            await using var env = await TradeTestEnv.CreateAsync(fileBackedDb: true);
            var tradeId = await env.ProposeStandardAsync();
            var context = env.NewFileContext(out var connection);
            try
            {
                var friendService = new FriendService(context);
                var removeTask = Task.Run(async () => { try { return (bool?)await friendService.RemoveAsync(env.A, env.B); } catch (SqliteException) { return null; } });
                var acceptTask = Task.Run(() => env.Service.AcceptAsync(env.B, tradeId));
                await Task.WhenAll(removeTask, acceptTask);

                // Removal itself never fails on the concurrent accept (no DbUpdateConcurrencyException).
                Assert.NotNull(await removeTask);
                var trade = await env.GetTradeAsync(tradeId);
                Assert.NotEqual(TradeStatus.Pending, trade.Status);
                Assert.Equal(0, await env.Db.Friendships.CountAsync());
            }
            finally
            {
                await context.DisposeAsync();
                await connection.DisposeAsync();
            }
        }
    }

    // --- List / pending badge ---

    [Fact]
    public async Task List_separates_friends_incoming_and_outgoing()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("me");
        var (friend, _) = await db.AddUserAsync("friend");
        var (incoming, _) = await db.AddUserAsync("incoming");
        var (outgoing, _) = await db.AddUserAsync("outgoing");
        var service = new FriendService(db.DbContext);
        await service.SendRequestAsync(me, friend);
        await service.AcceptAsync(friend, Assert.Single((await service.ListAsync(friend)).Incoming).FriendshipId);
        await service.SendRequestAsync(incoming, me);
        await service.SendRequestAsync(me, outgoing);

        var lists = await service.ListAsync(me);

        Assert.Equal(["friend"], lists.Friends.Select(f => f.UserName));
        Assert.Equal(["incoming"], lists.Incoming.Select(f => f.UserName));
        Assert.Equal(["outgoing"], lists.Outgoing.Select(f => f.UserName));
        Assert.Equal(1, await service.CountIncomingRequestsAsync(me));
        Assert.Equal(0, await service.CountIncomingRequestsAsync(outgoing + "x"));
    }

    [Fact]
    public async Task List_for_user_without_relationships_is_empty()
    {
        await using var db = await TestAppDb.CreateAsync();
        var (me, _) = await db.AddUserAsync("me");
        var service = new FriendService(db.DbContext);

        var lists = await service.ListAsync(me);

        Assert.Empty(lists.Friends);
        Assert.Empty(lists.Incoming);
        Assert.Empty(lists.Outgoing);
    }
}
