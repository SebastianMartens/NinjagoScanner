using System.Collections.Concurrent;

namespace NinjagoScanner.PictureService;

/// <summary>
/// In-memory, write-through cache for sidecar file contents, keyed by resolved sidecar file path.
/// Sits in front of <see cref="SidecarStore"/> so callers avoid re-reading unchanged sidecars from
/// disk on every request; every successful write updates the cache with the value that was just
/// persisted. Read failures (e.g. corrupt JSON) are never cached, so they are retried on next read.
/// </summary>
internal sealed class SidecarCache
{
    private readonly ConcurrentDictionary<string, SidecarRecord?> entries = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SidecarRecord?> GetAsync(string sidecarPath, CancellationToken cancellationToken)
    {
        var key = NormalizeKey(sidecarPath);
        if (entries.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var record = await SidecarStore.ReadRecordAsync(sidecarPath, cancellationToken);
        entries[key] = record;
        return record;
    }

    public async Task SetAsync(string sidecarPath, SidecarRecord record, CancellationToken cancellationToken)
    {
        await SidecarStore.WriteRecordAsync(sidecarPath, record, cancellationToken);
        entries[NormalizeKey(sidecarPath)] = record;
    }

    public async Task SetAsync(string sidecarPath, CardAnalysisResult result, CancellationToken cancellationToken)
    {
        await SidecarStore.WriteAsync(sidecarPath, result, cancellationToken);
        entries[NormalizeKey(sidecarPath)] = ToSidecarRecord(result);
    }

    private static string NormalizeKey(string sidecarPath)
    {
        return Path.GetFullPath(sidecarPath);
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
            SourceFilePath = result.SourceFilePath,
            SidecarFilePath = result.SidecarFilePath,
            AiModel = result.AiModel,
            RawModelResponse = result.RawModelResponse
        };
    }
}
