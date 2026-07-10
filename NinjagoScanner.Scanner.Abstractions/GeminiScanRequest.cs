namespace NinjagoScanner.Scanner.Abstractions;

public sealed class GeminiScanRequest
{
    public string? ApiKey { get; init; }
    public string? Model { get; init; }
    public string? CardPhotosDirectory { get; init; }
    public string? SeriesCatalogPath { get; init; }
    public bool? OverwriteExistingSidecars { get; init; }
    public int? DelayBetweenRequestsMs { get; init; }
    public int? RetryDelayMs { get; init; }
    public int? MaxAttempts { get; init; }
    public int? TimeoutSeconds { get; init; }
}
