using System.Text.Json;
using System.Text.Json.Serialization;

namespace NinjagoScanner.Scanner;

internal static class AnalysisStatuses
{
    public const string Ok = "ok";
    public const string Uncertain = "uncertain";
    public const string Failed = "failed";
}

internal sealed class CardAnalysisResult
{
    public required string Status { get; init; }
    public required string SourceFileName { get; init; }
    public required string SourceFilePath { get; init; }
    public required string SidecarFilePath { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public IReadOnlyList<string> DetectedText { get; init; } = Array.Empty<string>();
    public required string AiModel { get; init; }
    public required DateTimeOffset ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string? RawModelResponse { get; init; }
}

internal sealed class GeminiCardPayload
{
    public string? Status { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public string[]? DetectedText { get; init; }
}

internal sealed class SeriesInfo
{
    public required string Serie { get; init; }

    public int Jahr { get; init; }

    public string[] Besonderheiten { get; init; } = Array.Empty<string>();

    public string[] Sondereditionen { get; init; } = Array.Empty<string>();

    public string[] CardNames { get; init; } = Array.Empty<string>();
}

internal sealed class GeminiResponseEnvelope
{
    public GeminiCandidate[]? Candidates { get; init; }
}

internal sealed class GeminiCandidate
{
    public GeminiContent? Content { get; init; }
}

internal sealed class GeminiContent
{
    public GeminiPart[]? Parts { get; init; }
}

internal sealed class GeminiPart
{
    public string? Text { get; init; }
}

internal static class ScannerJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static readonly JsonSerializerOptions Pretty = new(Default)
    {
        WriteIndented = true
    };
}
