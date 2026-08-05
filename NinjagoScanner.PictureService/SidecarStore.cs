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
        await using var stream = File.OpenRead(sidecarPath);
        return await JsonSerializer.DeserializeAsync<SidecarRecord>(stream, ScannerJsonOptions.Default, cancellationToken);
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
    public string? Status { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
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
