using System.Collections.Concurrent;

namespace NinjagoScanner.PictureService;

/// <summary>
/// In-memory, write-through cache for sidecar records, keyed by the combination of collection ID
/// and photo ID (see picture-service-sidecar-cache's "Cache entries are keyed by collection and
/// photo together"). Sits in front of <see cref="SidecarTable"/> so callers avoid re-reading
/// unchanged records from DynamoDB on every request; every successful write updates the cache
/// with the value that was just persisted. Read failures are never cached, so they are retried on
/// next read.
/// </summary>
internal sealed class SidecarCache
{
    private readonly ISidecarStore sidecarTable;
    private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), SidecarRecord?> entries = new();

    public SidecarCache(ISidecarStore sidecarTable)
    {
        this.sidecarTable = sidecarTable;
    }

    public async Task<SidecarRecord?> GetAsync(string collectionId, string photoId, CancellationToken cancellationToken)
    {
        var key = (collectionId, photoId);
        if (entries.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var record = await sidecarTable.GetAsync(collectionId, photoId, cancellationToken);
        entries[key] = record;
        return record;
    }

    public async Task SetAsync(string collectionId, string photoId, SidecarRecord record, CancellationToken cancellationToken)
    {
        await sidecarTable.PutAsync(collectionId, photoId, record, cancellationToken);
        entries[(collectionId, photoId)] = record;
    }

    public async Task SetAsync(string collectionId, string photoId, CardAnalysisResult result, CancellationToken cancellationToken)
    {
        var record = ToSidecarRecord(result);
        await sidecarTable.PutAsync(collectionId, photoId, record, cancellationToken);
        entries[(collectionId, photoId)] = record;
    }

    public async Task RemoveAsync(string collectionId, string photoId, CancellationToken cancellationToken)
    {
        await sidecarTable.DeleteAsync(collectionId, photoId, cancellationToken);
        entries.TryRemove((collectionId, photoId), out _);
    }

    /// <summary>
    /// Enumerates every sidecar record within one collection, populating the cache along the way.
    /// Used by the bulk Scan RPC, where the store is authoritative for that collection and should
    /// overwrite whatever is cached.
    /// </summary>
    public async IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListByCollectionAsync(
        string collectionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (photoId, record) in sidecarTable.ListByCollectionAsync(collectionId, cancellationToken))
        {
            entries[(collectionId, photoId)] = record;
            yield return (photoId, record);
        }
    }

    /// <summary>
    /// Bulk-fills any not-yet-cached sidecar record for one collection from the store in a
    /// bounded, small number of requests (a paginated query), instead of leaving every uncached
    /// photo to be read one at a time. Unlike <see cref="ListByCollectionAsync"/>, an
    /// already-cached entry is left as-is rather than overwritten, so a value just written through
    /// this cache (see <see cref="SetAsync(string,string,SidecarRecord,CancellationToken)"/>)
    /// stays visible even if the store has since diverged out-of-band - used by ListCards, which
    /// reads through <see cref="GetAsync"/> afterward and must keep that read-your-own-writes
    /// guarantee.
    /// </summary>
    public async Task WarmFromStoreAsync(string collectionId, CancellationToken cancellationToken)
    {
        await foreach (var (photoId, record) in sidecarTable.ListByCollectionAsync(collectionId, cancellationToken))
        {
            entries.TryAdd((collectionId, photoId), record);
        }
    }

    /// <summary>
    /// Enumerates every sidecar record across every collection, populating the cache along the
    /// way. Used only by the global MigrateSidecars maintenance RPC (see
    /// collection-scoped-picture-access's deliberate exception for it).
    /// </summary>
    public async IAsyncEnumerable<(string CollectionId, string PhotoId, SidecarRecord Record)> ListAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (collectionId, photoId, record) in sidecarTable.ListAllAsync(cancellationToken))
        {
            entries[(collectionId, photoId)] = record;
            yield return (collectionId, photoId, record);
        }
    }

    private static SidecarRecord ToSidecarRecord(CardAnalysisResult result)
    {
        return new SidecarRecord
        {
            AnalysisStatus = result.AnalysisStatus,
            ReviewStatus = result.ReviewStatus,
            CardName = result.CardName,
            CardNumber = result.CardNumber,
            SetName = result.SetName,
            Rarity = result.Rarity,
            Language = result.Language,
            Confidence = result.Confidence,
            ReasoningSummary = result.ReasoningSummary,
            DetectedText = result.DetectedText.ToArray(),
            ScannedAtUtc = result.ScannedAtUtc,
            ErrorMessage = result.ErrorMessage,
            SourceFileName = result.SourceFileName,
            AiModel = result.AiModel,
            RawModelResponse = result.RawModelResponse
        };
    }
}
