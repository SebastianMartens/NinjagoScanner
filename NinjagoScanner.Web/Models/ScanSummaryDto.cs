namespace NinjagoScanner.Web.Models;

/// <summary>Result of PictureService's bulk-scan RPC, mirroring its ScanSummary.</summary>
public sealed class ScanSummaryDto
{
    public int TotalImages { get; init; }
    public int Processed { get; init; }
    public int Skipped { get; init; }
    public int Uncertain { get; init; }
    public int Failed { get; init; }
    public bool HasConfigurationError { get; init; }
    public bool StoppedEarly { get; init; }
    public string? Message { get; init; }
}
