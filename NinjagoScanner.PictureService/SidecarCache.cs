using System.Collections.Concurrent;

namespace NinjagoScanner.PictureService;

/// <summary>
/// In-memory, write-through cache for sidecar records, keyed by photo ID. Sits in front of
/// <see cref="SidecarTable"/> so callers avoid re-reading unchanged records from DynamoDB on
/// every request; every successful write updates the cache with the value that was just
/// persisted. Read failures are never cached, so they are retried on next read.
/// </summary>
internal sealed class SidecarCache
{
    private readonly ISidecarStore sidecarTable;
    private readonly ConcurrentDictionary<string, SidecarRecord?> entries = new(StringComparer.Ordinal);

    public SidecarCache(ISidecarStore sidecarTable)
    {
        this.sidecarTable = sidecarTable;
    }

    public async Task<SidecarRecord?> GetAsync(string photoId, CancellationToken cancellationToken)
    {
        if (entries.TryGetValue(photoId, out var cached))
        {
            return cached;
        }

        var record = await sidecarTable.GetAsync(photoId, cancellationToken);
        entries[photoId] = record;
        return record;
    }

    public async Task SetAsync(string photoId, SidecarRecord record, CancellationToken cancellationToken)
    {
        await sidecarTable.PutAsync(photoId, record, cancellationToken);
        entries[photoId] = record;
    }

    public async Task SetAsync(string photoId, CardAnalysisResult result, CancellationToken cancellationToken)
    {
        var record = ToSidecarRecord(result);
        await sidecarTable.PutAsync(photoId, record, cancellationToken);
        entries[photoId] = record;
    }

    public async Task RemoveAsync(string photoId, CancellationToken cancellationToken)
    {
        await sidecarTable.DeleteAsync(photoId, cancellationToken);
        entries.TryRemove(photoId, out _);
    }

    /// <summary>
    /// Enumerates every sidecar record, populating the cache along the way. Used by the bulk
    /// Scan/MigrateSidecars RPCs, where the store is authoritative and should overwrite whatever
    /// is cached.
    /// </summary>
    public async IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var (photoId, record) in sidecarTable.ListAllAsync(cancellationToken))
        {
            entries[photoId] = record;
            yield return (photoId, record);
        }
    }

    /// <summary>
    /// Bulk-fills any not-yet-cached sidecar record from the store in a bounded, small number of
    /// requests (a paginated scan), instead of leaving every uncached photo to be read one at a
    /// time. Unlike <see cref="ListAllAsync"/>, an already-cached entry is left as-is rather than
    /// overwritten, so a value just written through this cache (see <see cref="SetAsync"/>) stays
    /// visible even if the store has since diverged out-of-band - used by ListCards, which reads
    /// through <see cref="GetAsync"/> afterward and must keep that read-your-own-writes guarantee.
    /// </summary>
    public async Task WarmFromStoreAsync(CancellationToken cancellationToken)
    {
        await foreach (var (photoId, record) in sidecarTable.ListAllAsync(cancellationToken))
        {
            entries.TryAdd(photoId, record);
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
