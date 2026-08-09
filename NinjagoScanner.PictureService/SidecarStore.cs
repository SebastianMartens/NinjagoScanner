using System.Text;
using System.Text.Json;

namespace NinjagoScanner.PictureService;

internal static class SidecarStore
{
    public static string GetSidecarPath(string imagePath)
    {
        return imagePath + ".json";
    }

    public static async Task WriteAsync(string sidecarPath, CardAnalysisResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, ScannerJsonOptions.Pretty);
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, cancellationToken);
    }

    public static async Task<SidecarRecord?> ReadRecordAsync(string sidecarPath, CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(sidecarPath, Encoding.UTF8, cancellationToken);
        var record = JsonSerializer.Deserialize<SidecarRecord>(json, ScannerJsonOptions.Default);
        if (record is null || !string.IsNullOrWhiteSpace(record.AnalysisStatus))
        {
            return record;
        }

        using var document = JsonDocument.Parse(json);
        var legacyStatus = FindLegacyStatus(document.RootElement);
        return legacyStatus is null ? record : record with { AnalysisStatus = legacyStatus };
    }

    /// <summary>
    /// Sidecar files written before the "status" field was renamed to "AnalysisStatus" still carry
    /// the old key. This lets those pre-existing files keep surfacing a correct AnalysisStatus without
    /// requiring a rescan; it does not rewrite the file (see the MigrateSidecars RPC for that).
    /// </summary>
    private static string? FindLegacyStatus(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    public static async Task WriteRecordAsync(string sidecarPath, SidecarRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record, ScannerJsonOptions.Pretty);
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, cancellationToken);
    }
}

/// <summary>
/// Lenient, fully-optional representation of a sidecar JSON file, used when reading/merging
/// arbitrary sidecar contents (as opposed to <see cref="CardAnalysisResult"/> which is only written by the scanner).
/// </summary>
internal sealed record SidecarRecord
{
    public string? AnalysisStatus { get; init; }
    public string? ReviewStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public string? Language { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public string[]? DetectedText { get; init; }
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SourceFileName { get; init; }
    public string? SourceFilePath { get; init; }
    public string? SidecarFilePath { get; init; }
    public string? AiModel { get; init; }
    public string? RawModelResponse { get; init; }
}
