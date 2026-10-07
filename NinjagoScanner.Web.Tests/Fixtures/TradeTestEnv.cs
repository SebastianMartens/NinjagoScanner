using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// In-process world for trade tests: real CatalogService host, the fake PictureService host with
/// real move semantics, and a migrated SQLite database with users A, B (friends) and C (no
/// friends). Photo seeds (series are catalog "Serie 2" / "Serie 10"):
///   A: a4a, a4b (duplicate #4), a5 (single #5), la1, la2 (duplicate legendary #2 of Serie 10)
///   B: b6a, b6b, b6c (triplicate #6), b4 (single #4), bx (unmapped)
/// </summary>
internal sealed class TradeTestEnv : IAsyncDisposable
{
    static TradeTestEnv()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public CatalogServiceTestHost Catalog { get; } = new();
    public PictureServiceTestHost Pictures { get; } = new();
    public AppDbContext Db { get; private set; } = null!;
    public TradeService Service { get; private set; } = null!;
    public PictureServiceClient PictureClient { get; private set; } = null!;
    public CatalogServiceClient CatalogClient { get; private set; } = null!;

    public string A { get; private set; } = string.Empty;
    public string B { get; private set; } = string.Empty;
    public string C { get; private set; } = string.Empty;
    public string CollA { get; private set; } = string.Empty;
    public string CollB { get; private set; } = string.Empty;
    public string CollC { get; private set; } = string.Empty;

    private TestAppDb? memoryDb;
    private SqliteConnection? fileConnection;
    private string? filePath;

    public static async Task<TradeTestEnv> CreateAsync(bool fileBackedDb = false)
    {
        var env = new TradeTestEnv();
        await env.InitializeAsync(fileBackedDb);
        return env;
    }

    private async Task InitializeAsync(bool fileBackedDb)
    {
        Catalog.WriteCatalogFile("series_trade.json", """
        {
          "Serie_2": {
            "SortOrder": 2,
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Rarity": "common", "Karten": [
                {"Karten-Nr.": 4, "Name": {"de": "Cole"}},
                {"Karten-Nr.": 5, "Name": {"de": "Zane"}},
                {"Karten-Nr.": 6, "Name": {"de": "Jay"}}
              ] }
            }
          },
          "Serie_10": {
            "SortOrder": 10,
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Rarity": "common", "Karten": [
                {"Karten-Nr.": 1, "Name": {"de": "Wu"}},
                {"Karten-Nr.": 2, "Rarity": "legendary", "Name": {"de": "Golden Dragon"}}
              ] }
            }
          }
        }
        """);
        await Catalog.StartAsync();
        await Pictures.StartAsync();

        if (fileBackedDb)
        {
            filePath = Path.Combine(Path.GetTempPath(), $"trade-tests-{Guid.NewGuid():N}.db");
            Db = NewFileContext(out fileConnection);
            await Db.Database.MigrateAsync();
        }
        else
        {
            memoryDb = await TestAppDb.CreateAsync();
            Db = memoryDb.DbContext;
        }

        (A, CollA) = await AddUserAsync("alice");
        (B, CollB) = await AddUserAsync("bob");
        (C, CollC) = await AddUserAsync("carol");
        await MakeFriendsAsync(A, B);

        CatalogClient = new CatalogServiceClient(Catalog.Address);
        PictureClient = TestPictureServiceClientFactory.Create(Pictures.Address, Catalog.Address, 10 * 1024 * 1024);
        Service = new TradeService(Db, CatalogClient, PictureClient);

        Pictures.WritePhoto("a4a", Sidecar("Serie 2", "4", "Cole", "verified"), CollA);
        Pictures.WritePhoto("a4b", Sidecar("Serie 2", "4", "Cole"), CollA);
        Pictures.WritePhoto("a5", Sidecar("Serie 2", "5", "Zane"), CollA);
        Pictures.WritePhoto("la1", Sidecar("Serie 10", "2", "Golden Dragon"), CollA);
        Pictures.WritePhoto("la2", Sidecar("Serie 10", "2", "Golden Dragon"), CollA);
        Pictures.WritePhoto("b6a", Sidecar("Serie 2", "6", "Jay", "verified"), CollB);
        Pictures.WritePhoto("b6b", Sidecar("Serie 2", "6", "Jay"), CollB);
        Pictures.WritePhoto("b6c", Sidecar("Serie 2", "6", "Jay"), CollB);
        Pictures.WritePhoto("b4", Sidecar("Serie 2", "4", "Cole"), CollB);
        Pictures.WritePhoto("bx", """{ "analysisStatus": "uncertain", "reviewStatus": "unreviewed", "sourceFileName": "x.jpg" }""", CollB);
    }

    public static string Sidecar(string series, string number, string name, string review = "unreviewed") =>
        $$"""{ "analysisStatus": "ok", "reviewStatus": "{{review}}", "cardName": "{{name}}", "cardNumber": "{{number}}", "setName": "{{series}}", "language": "de", "sourceFileName": "{{name}}.jpg", "rotated180": true }""";

    public AppDbContext NewFileContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection($"Data Source={filePath}");
        connection.Open();
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }

    /// <summary>A second TradeService on its own DbContext (file-backed env only) for true concurrency.</summary>
    public (TradeService Service, AppDbContext Context, SqliteConnection Connection) NewConcurrentService()
    {
        var context = NewFileContext(out var connection);
        return (new TradeService(context, CatalogClient, PictureClient), context, connection);
    }

    public async Task<(string UserId, string CollectionId)> AddUserAsync(string userName, string? collectionId = null)
    {
        var user = new AppUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName}@example.test".ToUpperInvariant()
        };
        var collection = new Collection { Name = $"{userName} collection" };
        if (collectionId is not null)
        {
            collection.Id = collectionId;
        }

        Db.Users.Add(user);
        Db.Collections.Add(collection);
        Db.CollectionMemberships.Add(new CollectionMembership
        {
            CollectionId = collection.Id,
            UserId = user.Id,
            Role = CollectionRole.Owner
        });
        await Db.SaveChangesAsync();
        return (user.Id, collection.Id);
    }

    public async Task MakeFriendsAsync(string userA, string userB)
    {
        var (low, high) = Friendship.CanonicalPair(userA, userB);
        Db.Friendships.Add(new Friendship
        {
            RequesterUserId = userA,
            AddresseeUserId = userB,
            UserLowId = low,
            UserHighId = high,
            Status = FriendshipStatus.Accepted
        });
        await Db.SaveChangesAsync();
    }

    /// <summary>Alice offers a4a, Bob gives b6a.</summary>
    public async Task<string> ProposeStandardAsync()
    {
        var result = await Service.ProposeAsync(A, B, ["a4a"], ["b6a"]);
        Assert.True(result.Success, result.Message);
        return result.TradeId!;
    }

    /// <summary>Deletes a photo from an arbitrary collection through the fake's DeletePhoto RPC.</summary>
    public async Task DeletePhotoAsync(string collectionId, string photoId)
    {
        using var channel = Grpc.Net.Client.GrpcChannel.ForAddress(Pictures.Address);
        var client = new NinjagoScanner.PictureService.Protos.CardPictureService.CardPictureServiceClient(channel);
        await client.DeletePhotoAsync(new NinjagoScanner.PictureService.Protos.DeletePhotoRequest
        {
            CollectionId = collectionId,
            PhotoId = photoId
        });
    }

    public async Task<Trade> GetTradeAsync(string tradeId) =>
        await Db.Trades.AsNoTracking().Include(t => t.Items).SingleAsync(t => t.Id == tradeId);

    public async Task<int> BonusXpAsync(string collectionId) =>
        await Db.GamificationProfiles.AsNoTracking()
            .Where(p => p.CollectionId == collectionId).Select(p => p.BonusXp).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<(string PhotoId, string? CardName, string? Number, string ReviewStatus)>> CardsAsync(string collectionId)
    {
        var entries = await PictureClient.ListCardEntriesForCollectionAsync(collectionId);
        return entries
            .Select(e => (e.PhotoId, (string?)e.CardName, (string?)e.CardNumber, e.ReviewStatus))
            .OrderBy(e => e.PhotoId, StringComparer.Ordinal)
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (memoryDb is not null)
        {
            await memoryDb.DisposeAsync();
        }
        else
        {
            await Db.DisposeAsync();
            if (fileConnection is not null)
            {
                await fileConnection.DisposeAsync();
            }

            SqliteConnection.ClearAllPools();
            try
            {
                if (filePath is not null)
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
            }
        }

        await Pictures.DisposeAsync();
        await Catalog.DisposeAsync();
    }
}
