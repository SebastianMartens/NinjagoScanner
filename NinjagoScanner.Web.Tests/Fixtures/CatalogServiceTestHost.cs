using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Hosts a real, in-process <see cref="CardCatalogGrpcService"/> over cleartext HTTP/2 on a
/// loopback port, backed by a temp directory of catalog JSON files, so tests can point
/// <c>CardCatalogService</c> at a real gRPC endpoint without a separately-run process.
/// </summary>
public sealed class CatalogServiceTestHost : IAsyncDisposable
{
    private readonly string catalogDirectory = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerWebTests_Catalog_{Guid.NewGuid():N}");

    private WebApplication? app;

    public string Address { get; private set; } = string.Empty;

    public CatalogServiceTestHost()
    {
        Directory.CreateDirectory(catalogDirectory);
    }

    public void WriteCatalogFile(string fileName, string json)
    {
        File.WriteAllText(Path.Combine(catalogDirectory, fileName), json);
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Catalog:Directory"] = catalogDirectory
        });

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<CatalogRepository>();

        app = builder.Build();
        app.MapGrpcService<CardCatalogGrpcService>();

        await app.StartAsync();

        var addressesFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Address = addressesFeature!.Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        if (Directory.Exists(catalogDirectory))
        {
            Directory.Delete(catalogDirectory, recursive: true);
        }
    }
}
