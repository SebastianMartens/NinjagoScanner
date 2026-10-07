using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>web-trade-log "Trade log page": own trades only, newest first, immutable snapshots.</summary>
public class TradeLogServiceTests
{
    private static async Task<string> CompleteTradeAsync(TradeTestEnv env, string give, string receive)
    {
        var propose = await env.Service.ProposeAsync(env.A, env.B, [give], [receive]);
        Assert.True(propose.Success, propose.Message);
        var accept = await env.Service.AcceptAsync(env.B, propose.TradeId!);
        Assert.True(accept.Success, accept.Message);
        return propose.TradeId!;
    }

    [Fact]
    public async Task Completed_trade_lists_partner_date_and_cards_from_each_perspective()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await CompleteTradeAsync(env, "a4a", "b6a");
        var log = new TradeLogService(env.Db);

        var alice = Assert.Single(await log.ListAsync(env.A));
        var bob = Assert.Single(await log.ListAsync(env.B));

        Assert.Equal("bob", alice.PartnerUserName);
        Assert.Equal(TradeStatus.Completed, alice.Outcome);
        Assert.Equal("Cole", Assert.Single(alice.Given).CardName);
        Assert.Equal("Jay", Assert.Single(alice.Received).CardName);
        Assert.True(Math.Abs((DateTime.UtcNow - alice.DateUtc).TotalMinutes) < 5);
        Assert.Equal("alice", bob.PartnerUserName);
        Assert.Equal("Jay", Assert.Single(bob.Given).CardName);
        Assert.Equal("Cole", Assert.Single(bob.Received).CardName);
    }

    [Fact]
    public async Task Only_own_trades_are_listed()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await CompleteTradeAsync(env, "a4a", "b6a");

        Assert.Empty(await new TradeLogService(env.Db).ListAsync(env.C));
    }

    [Fact]
    public async Task Trades_are_listed_newest_first()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var first = await CompleteTradeAsync(env, "a4a", "b6a");
        var second = await CompleteTradeAsync(env, "la1", "b6b");
        var entry = await env.Db.TradeLogEntries.SingleAsync(e => e.TradeId == first);
        entry.CompletedAtUtc = entry.CompletedAtUtc.AddDays(-3);
        await env.Db.SaveChangesAsync();

        var rows = await new TradeLogService(env.Db).ListAsync(env.A);

        Assert.Equal([second, first], rows.Select(r => r.TradeId));
    }

    [Fact]
    public async Task Declined_and_cancelled_trades_show_their_outcome_without_a_log_entry()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var declined = (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).TradeId!;
        Assert.True((await env.Service.DeclineAsync(env.B, declined)).Success);
        var cancelled = (await env.Service.ProposeAsync(env.A, env.B, ["a4a"], ["b6a"])).TradeId!;
        Assert.True((await env.Service.CancelAsync(env.A, cancelled)).Success);

        var rows = await new TradeLogService(env.Db).ListAsync(env.A);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.TradeId == declined && r.Outcome == TradeStatus.Declined);
        Assert.Contains(rows, r => r.TradeId == cancelled && r.Outcome == TradeStatus.Cancelled);
        Assert.Empty(await env.Db.TradeLogEntries.ToListAsync());
    }

    [Fact]
    public async Task Failed_stale_trade_is_shown_as_failed()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        var id = await env.ProposeStandardAsync();
        await env.DeletePhotoAsync(env.CollA, "a4a");
        var accept = await env.Service.AcceptAsync(env.B, id);
        Assert.False(accept.Success);

        var rows = await new TradeLogService(env.Db).ListAsync(env.B);

        var row = Assert.Single(rows);
        Assert.Equal(TradeStatus.Failed, row.Outcome);
        Assert.Equal("alice", row.PartnerUserName);
    }

    [Fact]
    public async Task Open_pending_trades_are_not_part_of_the_log()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await env.ProposeStandardAsync();

        Assert.Empty(await new TradeLogService(env.Db).ListAsync(env.A));
    }

    [Fact]
    public async Task Entry_survives_friendship_removal_and_username_change()
    {
        await using var env = await TradeTestEnv.CreateAsync();
        await CompleteTradeAsync(env, "a4a", "b6a");
        await new FriendService(env.Db).RemoveAsync(env.A, env.B);
        var bob = await env.Db.Users.SingleAsync(u => u.Id == env.B);
        bob.UserName = "robert";
        await env.Db.SaveChangesAsync();

        var row = Assert.Single(await new TradeLogService(env.Db).ListAsync(env.A));

        Assert.Equal("bob", row.PartnerUserName);
        Assert.Equal("Cole", Assert.Single(row.Given).CardName);
    }

    [Fact]
    public void Log_service_exposes_no_write_operations()
    {
        var methods = typeof(TradeLogService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        Assert.Equal(["ListAsync"], methods);
    }
}
