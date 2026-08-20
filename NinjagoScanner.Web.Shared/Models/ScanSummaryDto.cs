namespace NinjagoScanner.Web.Shared.Models;

/// <summary>Result of the BFF's bulk-scan endpoint (POST /api/scan), mirroring PictureService's ScanSummary.</summary>
public sealed class ScanSummaryDto
{
    public int TotalImages { get; init; }
    public int Processed { get; init; }
    public int Skipped { get; init; }
    public int Uncertain { get; init; }
    public int Failed { get; init; }
    public bool HasConfigurationError { get; init; }
    public string? Message { get; init; }
}
