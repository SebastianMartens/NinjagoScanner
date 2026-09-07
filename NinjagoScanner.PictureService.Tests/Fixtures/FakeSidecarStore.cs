using System.Collections.Concurrent;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Fixtures;

/// <summary>
/// In-memory stand-in for <see cref="ISidecarStore"/> (DynamoDB in production), so tests can
/// exercise <see cref="SidecarCache"/> and <see cref="NinjagoScanner.PictureService.Services.PictureScannerGrpcService"/>
/// without real AWS credentials. Keyed by (collectionId, photoId) to match the production table's
/// CollectionId/PhotoId key schema. Exposes <see cref="Tamper"/> to simulate a write to the
/// underlying store that bypasses the cache, for cache-consistency tests.
/// </summary>
internal sealed class FakeSidecarStore : ISidecarStore
{
    private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), SidecarRecord> records = new();
    private readonly HashSet<(string CollectionId, string PhotoId)> failOnceKeys = new();

    /// <summary>Number of times <see cref="GetAsync"/> has been called, for asserting bulk-vs-per-photo read patterns.</summary>
    public int GetAsyncCallCount { get; private set; }

    /// <summary>Number of times <see cref="ListByCollectionAsync"/> has been enumerated, for asserting bulk-vs-per-photo read patterns.</summary>
    public int ListByCollectionAsyncCallCount { get; private set; }

    public Task<SidecarRecord?> GetAsync(string collectionId, string photoId, CancellationToken cancellationToken)
    {
        GetAsyncCallCount++;

        var key = (collectionId, photoId);
        if (failOnceKeys.Remove(key))
        {
            throw new InvalidOperationException($"Simulated read failure for '{collectionId}/{photoId}'.");
        }

        return Task.FromResult(records.TryGetValue(key, out var record) ? record : null);
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
        ListByCollectionAsyncCallCount++;

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

    public bool ContainsKey(string collectionId, string photoId) => records.ContainsKey((collectionId, photoId));

    /// <summary>Directly overwrites a record, bypassing whatever cache sits in front of this store.</summary>
    public void Tamper(string collectionId, string photoId, SidecarRecord record) => records[(collectionId, photoId)] = record;

    /// <summary>Makes the next <see cref="GetAsync"/> call for this key throw, then behave normally.</summary>
    public void FailNextReadFor(string collectionId, string photoId) => failOnceKeys.Add((collectionId, photoId));
}
