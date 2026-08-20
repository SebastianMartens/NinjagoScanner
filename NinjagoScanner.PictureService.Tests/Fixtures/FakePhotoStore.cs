using System.Collections.Concurrent;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Fixtures;

/// <summary>
/// In-memory stand-in for <see cref="IPhotoStore"/> (S3 in production), so tests can exercise
/// photo existence/read/delete without real AWS credentials.
/// </summary>
internal sealed class FakePhotoStore : IPhotoStore
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
        foreach (var photoId in objects.Keys)
        {
            yield return photoId;
        }

        await Task.CompletedTask;
    }
}
