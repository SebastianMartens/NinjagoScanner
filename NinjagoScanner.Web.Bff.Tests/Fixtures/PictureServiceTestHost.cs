using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

namespace NinjagoScanner.Web.Bff.Tests.Fixtures;

/// <summary>
/// Hosts a real, in-process <see cref="PictureScannerGrpcService"/> over cleartext HTTP/2 on a
/// loopback port, backed by in-memory fakes of <see cref="IPhotoStore"/> and <see cref="ISidecarStore"/>
/// (S3/DynamoDB in production), so tests can point the BFF's gRPC-calling logic at a real gRPC
/// endpoint without a separately-run process or real AWS credentials.
/// </summary>
public sealed class PictureServiceTestHost : IAsyncDisposable
{
    // ListCards only recognizes photos that exist in the photo store; content is irrelevant.
    private static readonly byte[] DummyImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    private readonly InMemoryPhotoStore photoStore = new();
    private readonly InMemorySidecarStore sidecarStore = new();

    private WebApplication? app;

    public string Address { get; private set; } = string.Empty;

    public void WritePhoto(string photoId, string? sidecarJson = null)
    {
        photoStore.Seed(photoId, DummyImageBytes);

        if (sidecarJson is not null)
        {
            var record = JsonSerializer.Deserialize<SidecarRecord>(sidecarJson, ScannerJsonOptions.Default);
            if (record is not null)
            {
                sidecarStore.Seed(photoId, record);
            }
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
        builder.Services.AddSingleton<IPhotoStore>(photoStore);
        builder.Services.AddSingleton<ISidecarStore>(sidecarStore);
        builder.Services.AddSingleton<SidecarCache>();
        builder.Services.AddScoped(provider => new PictureScannerGrpcService(
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<ILogger<PictureScannerGrpcService>>(),
            provider.GetRequiredService<SidecarCache>(),
            provider.GetRequiredService<IPhotoStore>()));

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
    }

    private sealed class InMemoryPhotoStore : IPhotoStore
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

        public void Seed(string photoId, byte[] bytes) => objects[photoId] = bytes;

        public Task<byte[]> GetBytesAsync(string photoId, CancellationToken cancellationToken)
        {
            return objects.TryGetValue(photoId, out var bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException($"No fake photo bytes seeded for '{photoId}'.");
        }

        public Task<bool> ExistsAsync(string photoId, CancellationToken cancellationToken)
        {
            return Task.FromResult(objects.ContainsKey(photoId));
        }

        public Task DeleteAsync(string photoId, CancellationToken cancellationToken)
        {
            objects.TryRemove(photoId, out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ListPhotoIdsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var photoId in objects.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                yield return photoId;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class InMemorySidecarStore : ISidecarStore
    {
        private readonly ConcurrentDictionary<string, SidecarRecord> records = new(StringComparer.Ordinal);

        public void Seed(string photoId, SidecarRecord record) => records[photoId] = record;

        public Task<SidecarRecord?> GetAsync(string photoId, CancellationToken cancellationToken)
        {
            return Task.FromResult(records.TryGetValue(photoId, out var record) ? record : null);
        }

        public Task PutAsync(string photoId, SidecarRecord record, CancellationToken cancellationToken)
        {
            records[photoId] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string photoId, CancellationToken cancellationToken)
        {
            records.TryRemove(photoId, out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var pair in records)
            {
                yield return (pair.Key, pair.Value);
            }

            await Task.CompletedTask;
        }
    }
}
