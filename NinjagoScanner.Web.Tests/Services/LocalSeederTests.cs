using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Seeding;

namespace NinjagoScanner.Web.Tests.Services;

public class LocalSeederTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireDigit = true;
            })
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<LocalSeeder>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static SeedManifest CreateManifest() => new()
    {
        Password = "Test-1234",
        Users =
        [
            new SeedUser { UserName = "alice", CollectionId = "11111111111111111111111111111111" },
            new SeedUser { UserName = "bob", CollectionId = "22222222222222222222222222222222" }
        ],
        Friendship = new SeedFriendship { Requester = "alice", Addressee = "bob", Status = FriendshipStatus.Accepted },
        PendingTrade = new SeedTrade
        {
            Id = "33333333333333333333333333333333",
            Proposer = "alice",
            Recipient = "bob",
            Items =
            [
                new SeedTradeItem { Side = TradeSide.FromProposer, PhotoId = "photo-a", SeriesName = "S1", CardNumber = "1", CardName = "A", Rarity = "Common" },
                new SeedTradeItem { Side = TradeSide.FromRecipient, PhotoId = "photo-b", SeriesName = "S1", CardNumber = "2", CardName = "B", Rarity = "Common" }
            ]
        }
    };

    private async Task SeedAsync()
    {
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LocalSeeder>().SeedAsync(CreateManifest());
    }

    [Fact]
    public async Task SeedAsync_CreatesUsersCollectionsFriendshipAndPendingTrade()
    {
        await SeedAsync();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.True(await db.Collections.AnyAsync(c => c.Id == "11111111111111111111111111111111"));
        Assert.Equal(2, await db.CollectionMemberships.CountAsync(m => m.Role == CollectionRole.Owner));
        var friendship = await db.Friendships.SingleAsync();
        Assert.Equal(FriendshipStatus.Accepted, friendship.Status);
        var trade = await db.Trades.Include(t => t.Items).SingleAsync();
        Assert.Equal(TradeStatus.Pending, trade.Status);
        Assert.Equal(2, trade.Items.Count);
        Assert.All(trade.Items, item => Assert.True(item.Reserved));
    }

    [Fact]
    public async Task SeedAsync_UsersCanSignInWithTheKnownPassword()
    {
        await SeedAsync();

        using var scope = _provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var alice = await userManager.FindByNameAsync("alice");
        Assert.NotNull(alice);
        Assert.True(await userManager.CheckPasswordAsync(alice, "Test-1234"));
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateRecords()
    {
        await SeedAsync();
        await SeedAsync();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.Collections.CountAsync());
        Assert.Equal(1, await db.Friendships.CountAsync());
        Assert.Equal(1, await db.Trades.CountAsync());
        Assert.Equal(2, await db.TradeItems.CountAsync());
    }
}

public class SeedModeTests
{
    [Fact]
    public void IsRequested_DetectsSeedArgument() =>
        Assert.True(Seeding.SeedMode.IsRequested(["--urls", "x", "seed"]));

    [Fact]
    public void IsRequested_FalseWithoutSeedArgument() =>
        Assert.False(Seeding.SeedMode.IsRequested(["--urls", "x"]));

    [Theory]
    [InlineData("Data/users.db")]
    [InlineData("./Data/users.db")]
    public void EnsureNotDefaultDatabase_RefusesDefaultPath(string path) =>
        Assert.Throws<InvalidOperationException>(() => Seeding.SeedMode.EnsureNotDefaultDatabase(path));

    [Fact]
    public void EnsureNotDefaultDatabase_AllowsSeparateFile() =>
        Seeding.SeedMode.EnsureNotDefaultDatabase(Path.Combine(Path.GetTempPath(), "ninjago-seed.db"));
}
