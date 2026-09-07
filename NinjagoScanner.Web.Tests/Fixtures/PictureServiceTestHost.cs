using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Hosts a real, in-process <see cref="PictureScannerGrpcService"/> over cleartext HTTP/2 on a
/// loopback port, backed by in-memory fakes of <see cref="IPhotoStore"/> and <see cref="ISidecarStore"/>
/// (S3/DynamoDB in production), so tests can point the Web app's gRPC-calling logic at a real gRPC
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

    public void WritePhoto(string photoId, string? sidecarJson = null, string? collectionId = null)
    {
        var resolvedCollectionId = collectionId ?? TestCurrentCollectionContext.CollectionId;
        photoStore.Seed(resolvedCollectionId, photoId, DummyImageBytes);

        if (sidecarJson is not null)
        {
            var record = JsonSerializer.Deserialize<SidecarRecord>(sidecarJson, ScannerJsonOptions.Default);
            if (record is not null)
            {
                sidecarStore.Seed(resolvedCollectionId, photoId, record);
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
        private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), byte[]> objects = new();

        public void Seed(string collectionId, string photoId, byte[] bytes) => objects[(collectionId, photoId)] = bytes;

        public Task PutBytesAsync(string collectionId, string photoId, byte[] bytes, CancellationToken cancellationToken)
        {
            objects[(collectionId, photoId)] = bytes;
            return Task.CompletedTask;
        }

        public Task<string> CreateDownloadUrlAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            return Task.FromResult($"https://fake-photo-store.test/{photoId}");
        }

        public Task<byte[]> GetBytesAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            return objects.TryGetValue((collectionId, photoId), out var bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException($"No fake photo bytes seeded for '{collectionId}/{photoId}'.");
        }

        public Task<bool> ExistsAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            return Task.FromResult(objects.ContainsKey((collectionId, photoId)));
        }

        public Task DeleteAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            objects.TryRemove((collectionId, photoId), out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<string> ListPhotoIdsAsync(
            string collectionId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var key in objects.Keys.Where(key => key.CollectionId == collectionId).OrderBy(key => key.PhotoId, StringComparer.OrdinalIgnoreCase))
            {
                yield return key.PhotoId;
            }

            await Task.CompletedTask;
        }
    }

    private sealed class InMemorySidecarStore : ISidecarStore
    {
        private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), SidecarRecord> records = new();

        public void Seed(string collectionId, string photoId, SidecarRecord record) => records[(collectionId, photoId)] = record;

        public Task<SidecarRecord?> GetAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            return Task.FromResult(records.TryGetValue((collectionId, photoId), out var record) ? record : null);
        }

        public Task PutAsync(string collectionId, string photoId, SidecarRecord record, CancellationToken cancellationToken)
        {
            records[(collectionId, photoId)] = record;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string collectionId, string photoId, CancellationToken cancellationToken)
        {
            records.TryRemove((collectionId, photoId), out _);
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListByCollectionAsync(
            string collectionId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var pair in records)
            {
                if (pair.Key.CollectionId == collectionId)
                {
                    yield return (pair.Key.PhotoId, pair.Value);
                }
            }

            await Task.CompletedTask;
        }

        public async IAsyncEnumerable<(string CollectionId, string PhotoId, SidecarRecord Record)> ListAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var pair in records)
            {
                yield return (pair.Key.CollectionId, pair.Key.PhotoId, pair.Value);
            }

            await Task.CompletedTask;
        }
    }
}
