namespace NinjagoScanner.Scanner.Abstractions;

public sealed class GeminiScanSummary
{
    public int TotalImages { get; init; }
    public int Processed { get; init; }
    public int Skipped { get; init; }
    public int Uncertain { get; init; }
    public int Failed { get; init; }
    public bool HasConfigurationError { get; init; }
    public string? Message { get; init; }
}
