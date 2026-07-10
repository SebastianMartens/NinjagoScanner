namespace NinjagoScanner.Scanner.Abstractions;

public interface IGeminiCardScanner
{
    Task<GeminiScanSummary> ScanAsync(GeminiScanRequest? request = null, CancellationToken cancellationToken = default);
}
