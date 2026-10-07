using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Fresh in-memory SQLite AppDbContext created by applying the real EF migrations (so tests also
/// prove the migrations produce a working schema). The connection stays open for the lifetime of
/// the instance, since an in-memory SQLite database is dropped when its connection closes.
/// </summary>
internal sealed class TestAppDb : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestAppDb(SqliteConnection connection, AppDbContext dbContext)
    {
        _connection = connection;
        DbContext = dbContext;
    }

    public AppDbContext DbContext { get; }

    public static async Task<TestAppDb> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
        return new TestAppDb(connection, dbContext);
    }

    /// <summary>Creates a user plus an owned collection; returns (userId, collectionId).</summary>
    public async Task<(string UserId, string CollectionId)> AddUserAsync(string userName)
    {
        var user = new AppUser
        {
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.test",
            NormalizedEmail = $"{userName}@example.test".ToUpperInvariant()
        };
        var collection = new Collection { Name = $"{userName} collection" };
        DbContext.Users.Add(user);
        DbContext.Collections.Add(collection);
        DbContext.CollectionMemberships.Add(new CollectionMembership
        {
            CollectionId = collection.Id,
            UserId = user.Id,
            Role = CollectionRole.Owner
        });
        await DbContext.SaveChangesAsync();
        return (user.Id, collection.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
