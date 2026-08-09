using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Hosts a real, in-process <see cref="PictureScannerGrpcService"/> over cleartext HTTP/2 on a
/// loopback port, backed by a temp directory of photo files (+ optional sidecar JSON), so tests
/// can point <c>CardCatalogService</c> at a real gRPC endpoint without a separately-run process.
/// </summary>
public sealed class PictureServiceTestHost : IAsyncDisposable
{
    // ListCards only recognizes files with a supported image extension; content is irrelevant.
    private static readonly byte[] DummyImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    public string CardPhotosDirectory { get; } = Path.Combine(
        Path.GetTempPath(),
        $"NinjagoScannerWebTests_Photos_{Guid.NewGuid():N}");

    private WebApplication? app;

    public string Address { get; private set; } = string.Empty;

    public PictureServiceTestHost()
    {
        Directory.CreateDirectory(CardPhotosDirectory);
    }

    public void WritePhoto(string imageFileName, string? sidecarJson = null)
    {
        File.WriteAllBytes(Path.Combine(CardPhotosDirectory, imageFileName), DummyImageBytes);

        if (sidecarJson is not null)
        {
            File.WriteAllText(Path.Combine(CardPhotosDirectory, imageFileName + ".json"), sidecarJson);
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<SidecarCache>();
        builder.Services.AddScoped(provider => new PictureScannerGrpcService(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<PictureScannerGrpcService>>(),
            provider.GetRequiredService<SidecarCache>()));

        app = builder.Build();
        app.MapGrpcService<PictureScannerGrpcService>();

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

        if (Directory.Exists(CardPhotosDirectory))
        {
            Directory.Delete(CardPhotosDirectory, recursive: true);
        }
    }
}
