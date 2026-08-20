using System.Collections.Concurrent;
using NinjagoScanner.PictureService;

namespace NinjagoScanner.PictureService.Tests.Fixtures;

/// <summary>
/// In-memory stand-in for <see cref="ISidecarStore"/> (DynamoDB in production), so tests can
/// exercise <see cref="SidecarCache"/> and <see cref="NinjagoScanner.PictureService.Services.PictureScannerGrpcService"/>
/// without real AWS credentials. Exposes <see cref="Tamper"/> to simulate a write to the
/// underlying store that bypasses the cache, for cache-consistency tests.
/// </summary>
internal sealed class FakeSidecarStore : ISidecarStore
{
    private readonly ConcurrentDictionary<string, SidecarRecord> records = new(StringComparer.Ordinal);
    private readonly HashSet<string> failOnceKeys = new(StringComparer.Ordinal);

    public Task<SidecarRecord?> GetAsync(string photoId, CancellationToken cancellationToken)
    {
        if (failOnceKeys.Remove(photoId))
        {
            throw new InvalidOperationException($"Simulated read failure for '{photoId}'.");
        }

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

    public bool ContainsKey(string photoId) => records.ContainsKey(photoId);

    /// <summary>Directly overwrites a record, bypassing whatever cache sits in front of this store.</summary>
    public void Tamper(string photoId, SidecarRecord record) => records[photoId] = record;

    /// <summary>Makes the next <see cref="GetAsync"/> call for this key throw, then behave normally.</summary>
    public void FailNextReadFor(string photoId) => failOnceKeys.Add(photoId);
}
