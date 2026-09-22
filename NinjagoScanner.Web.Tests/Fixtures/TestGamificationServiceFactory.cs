using Grpc.Net.Client;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Builds a GamificationService wired to a fixed test collection and a fresh in-memory SQLite
/// AppDbContext, for tests that don't specifically exercise collection resolution itself. The
/// SqliteConnection is kept open for the lifetime of the returned service - an in-memory SQLite
/// database is dropped the moment its connection closes.
/// </summary>
internal sealed class TestGamificationServiceHandle : IAsyncDisposable
{
    public required GamificationService Service { get; init; }
    public required AppDbContext DbContext { get; init; }
    internal required SqliteConnection Connection { get; init; }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

internal static class TestGamificationServiceFactory
{
    public static async Task<TestGamificationServiceHandle> CreateAsync(
        string catalogServiceAddress, string pictureServiceAddress, long maxUploadBytes = 10 * 1024 * 1024)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var catalogServiceClient = new CatalogServiceClient(catalogServiceAddress);
        var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var pictureServiceClient = new PictureServiceClient(
            channel,
            catalogServiceAddress,
            maxUploadBytes,
            new TestCurrentCollectionContext(),
            new TestAuthenticationStateProvider());

        var service = new GamificationService(
            catalogServiceClient,
            pictureServiceClient,
            dbContext,
            new TestCurrentCollectionContext(),
            new TestAuthenticationStateProvider());

        return new TestGamificationServiceHandle
        {
            Service = service,
            DbContext = dbContext,
            Connection = connection
        };
    }
}
