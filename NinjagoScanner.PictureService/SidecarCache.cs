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
    /// Enumerates every sidecar record, populating the cache along the way. Used by ListCards
    /// and the bulk Scan/MigrateSidecars RPCs.
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
