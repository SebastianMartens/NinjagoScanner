using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.PictureService.Services;

public sealed class PictureScannerGrpcService : PictureScanner.PictureScannerBase
{
    private readonly IConfiguration configuration;
    private readonly ILogger<PictureScannerGrpcService> logger;

    public PictureScannerGrpcService(IConfiguration configuration, ILogger<PictureScannerGrpcService> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    public override async Task<ScanSummary> Scan(ScanRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        var appConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddUserSecrets<PictureScannerGrpcService>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var config = ScannerConfig.Load(appConfiguration, request);

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return new ScanSummary
            {
                HasConfigurationError = true,
                Message = "GEMINI_API_KEY ist nicht gesetzt."
            };
        }

        if (!Directory.Exists(config.CardPhotosDirectory))
        {
            return new ScanSummary
            {
                HasConfigurationError = true,
                Message = $"Der Ordner '{config.CardPhotosDirectory}' wurde nicht gefunden."
            };
        }

        IReadOnlyList<SeriesInfo> seriesCatalog;
        try
        {
            seriesCatalog = await CatalogGrpcClient.LoadSeriesCatalogAsync(config.CatalogServiceAddress, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Katalog-Service unter {CatalogServiceAddress} nicht erreichbar", config.CatalogServiceAddress);
            return new ScanSummary
            {
                HasConfigurationError = true,
                Message = $"Der CatalogService unter '{config.CatalogServiceAddress}' ist nicht erreichbar."
            };
        }

        if (seriesCatalog.Count == 0)
        {
            return new ScanSummary
            {
                HasConfigurationError = true,
                Message = "Der CatalogService hat keine Seriendaten geliefert."
            };
        }

        var cardImages = Directory
            .EnumerateFiles(config.CardPhotosDirectory)
            .Where(path => ScannerConfig.SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cardImages.Count == 0)
        {
            return new ScanSummary
            {
                TotalImages = 0,
                Message = $"Im Ordner '{config.CardPhotosDirectory}' wurden keine Kartenbilder gefunden."
            };
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };

        var processedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        var uncertainCount = 0;

        for (var index = 0; index < cardImages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imagePath = cardImages[index];
            var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

            if (!config.OverwriteExistingSidecars && File.Exists(sidecarPath))
            {
                skippedCount++;
                continue;
            }

            CardAnalysisResult result;
            try
            {
                result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, seriesCatalog, imagePath, sidecarPath, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unerwarteter Fehler bei der Analyse von {ImagePath}", imagePath);
                result = new CardAnalysisResult
                {
                    Status = AnalysisStatuses.Failed,
                    SourceFileName = Path.GetFileName(imagePath),
                    SourceFilePath = imagePath,
                    SidecarFilePath = sidecarPath,
                    AiModel = config.Model,
                    ScannedAtUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = $"Unerwarteter Fehler: {exception.Message}",
                    DetectedText = Array.Empty<string>()
                };
            }

            await SidecarStore.WriteAsync(sidecarPath, result, cancellationToken);

            logger.LogDebug(
                "[{Index}/{Total}] {FileName} → Status: {Status} | Karte: {CardName} | Serie: {SetName} | Nr: {CardNumber}",
                index + 1,
                cardImages.Count,
                Path.GetFileName(imagePath),
                result.Status,
                string.IsNullOrWhiteSpace(result.CardName) ? "(unbekannt)" : result.CardName,
                result.SetName ?? "-",
                result.CardNumber ?? "-");

            processedCount++;
            if (string.Equals(result.Status, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                failedCount++;
            }
            else if (string.Equals(result.Status, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase))
            {
                uncertainCount++;
            }

            if (index < cardImages.Count - 1 && config.DelayBetweenRequestsMs > 0)
            {
                await Task.Delay(config.DelayBetweenRequestsMs, cancellationToken);
            }
        }

        return new ScanSummary
        {
            TotalImages = cardImages.Count,
            Processed = processedCount,
            Skipped = skippedCount,
            Uncertain = uncertainCount,
            Failed = failedCount,
            Message = "Batch abgeschlossen."
        };
    }
}
