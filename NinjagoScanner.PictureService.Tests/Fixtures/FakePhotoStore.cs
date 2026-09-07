using System.Collections.Concurrent;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Fixtures;

/// <summary>
/// In-memory stand-in for <see cref="IPhotoStore"/> (S3 in production), so tests can exercise
/// photo existence/read/delete without real AWS credentials. Keyed by (collectionId, photoId) to
/// match the production store's collection-partitioned key scheme.
/// </summary>
internal sealed class FakePhotoStore : IPhotoStore
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
        return Task.FromResult($"https://fake-bucket.example/{collectionId}/{photoId}");
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
        foreach (var key in objects.Keys)
        {
            if (key.CollectionId == collectionId)
            {
                yield return key.PhotoId;
            }
        }

        await Task.CompletedTask;
    }
}
