using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NinjagoScanner.PictureService.Protos;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinjagoScanner.PictureService.Services;

public sealed class PictureScannerGrpcService : CardPictureService.CardPictureServiceBase
{
    private const int MaxUploadFileNameAttempts = 100;

    private static readonly Regex UnsafeFileNameRegex = new("[^A-Za-z0-9_-]+", RegexOptions.Compiled);

    private readonly IConfiguration configuration;
    private readonly ILogger<PictureScannerGrpcService> logger;
    private readonly SidecarCache sidecarCache;

    internal PictureScannerGrpcService(IConfiguration configuration, ILogger<PictureScannerGrpcService> logger, SidecarCache sidecarCache)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.sidecarCache = sidecarCache;
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
                    AnalysisStatus = AnalysisStatuses.Failed,
                    SourceFileName = Path.GetFileName(imagePath),
                    SourceFilePath = imagePath,
                    SidecarFilePath = sidecarPath,
                    AiModel = config.Model,
                    ScannedAtUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = $"Unerwarteter Fehler: {exception.Message}",
                    DetectedText = Array.Empty<string>()
                };
            }

            if (File.Exists(sidecarPath))
            {
                var existingReviewStatus = (await sidecarCache.GetAsync(sidecarPath, cancellationToken))?.ReviewStatus;
                if (!string.IsNullOrWhiteSpace(existingReviewStatus))
                {
                    result = result with { ReviewStatus = existingReviewStatus };
                }
            }

            await sidecarCache.SetAsync(sidecarPath, result, cancellationToken);

            logger.LogDebug(
                "[{Index}/{Total}] {FileName} → Status: {AnalysisStatus} | Karte: {CardName} | Serie: {SetName} | Nr: {CardNumber}",
                index + 1,
                cardImages.Count,
                Path.GetFileName(imagePath),
                result.AnalysisStatus,
                string.IsNullOrWhiteSpace(result.CardName) ? "(unbekannt)" : result.CardName,
                result.SetName ?? "-",
                result.CardNumber ?? "-");

            processedCount++;
            if (string.Equals(result.AnalysisStatus, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                failedCount++;
            }
            else if (string.Equals(result.AnalysisStatus, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase))
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

    public override async Task<ListCardsResponse> ListCards(ListCardsRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);

        var response = new ListCardsResponse();
        if (!Directory.Exists(directory))
        {
            return response;
        }

        var imageFiles = Directory
            .EnumerateFiles(directory)
            .Where(path => ScannerConfig.SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imageFileName = Path.GetFileName(imagePath);
            var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

            if (!File.Exists(sidecarPath))
            {
                response.Cards.Add(new CardEntry
                {
                    ImageFileName = imageFileName,
                    AnalysisStatus = "pending",
                    ReviewStatus = ReviewStatuses.Unreviewed
                });
                continue;
            }

            try
            {
                var sidecar = await sidecarCache.GetAsync(sidecarPath, cancellationToken);
                response.Cards.Add(ToCardEntry(imageFileName, sidecar));
            }
            catch (Exception exception)
            {
                response.Cards.Add(new CardEntry
                {
                    ImageFileName = imageFileName,
                    AnalysisStatus = "failed",
                    ReviewStatus = ReviewStatuses.Unreviewed,
                    ErrorMessage = $"Sidecar konnte nicht gelesen werden: {exception.Message}"
                });
            }
        }

        return response;
    }

    public override async Task<UpdateSidecarResponse> UpdateSidecar(UpdateSidecarRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);
        var imagePath = Path.Combine(directory, request.ImageFileName);
        var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

        var existing = File.Exists(sidecarPath)
            ? await sidecarCache.GetAsync(sidecarPath, cancellationToken) ?? new SidecarRecord()
            : new SidecarRecord
            {
                SourceFileName = request.ImageFileName,
                SourceFilePath = imagePath,
                SidecarFilePath = sidecarPath
            };

        var updated = existing with
        {
            AnalysisStatus = NormalizeNullable(request.AnalysisStatus),
            CardName = NormalizeNullable(request.CardName),
            CardNumber = NormalizeNullable(request.CardNumber),
            SetName = NormalizeNullable(request.SetName),
            Rarity = NormalizeNullable(request.Rarity),
            Language = NormalizeNullable(request.Language),
            Confidence = request.Confidence,
            ReasoningSummary = NormalizeNullable(request.ReasoningSummary),
            DetectedText = request.DetectedText.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()).ToArray(),
            ErrorMessage = NormalizeNullable(request.ErrorMessage),
            ReviewStatus = NormalizeNullable(request.ReviewStatus),
            SourceFileName = existing.SourceFileName ?? request.ImageFileName,
            SourceFilePath = existing.SourceFilePath ?? imagePath,
            SidecarFilePath = existing.SidecarFilePath ?? sidecarPath
        };

        await sidecarCache.SetAsync(sidecarPath, updated, cancellationToken);

        return new UpdateSidecarResponse { Success = true };
    }

    public override async Task<UpdateSetNameResponse> UpdateSetName(UpdateSetNameRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);
        var imagePath = Path.Combine(directory, request.ImageFileName);
        var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

        var sidecar = File.Exists(sidecarPath)
            ? await sidecarCache.GetAsync(sidecarPath, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" }
            : new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { SetName = NormalizeNullable(request.SetName) };

        await sidecarCache.SetAsync(sidecarPath, sidecar, cancellationToken);

        return new UpdateSetNameResponse { Success = true };
    }

    public override async Task<UpdateCardNumberResponse> UpdateCardNumber(UpdateCardNumberRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);
        var imagePath = Path.Combine(directory, request.ImageFileName);
        var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

        var sidecar = File.Exists(sidecarPath)
            ? await sidecarCache.GetAsync(sidecarPath, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" }
            : new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { CardNumber = NormalizeNullable(request.CardNumber) };

        await sidecarCache.SetAsync(sidecarPath, sidecar, cancellationToken);

        return new UpdateCardNumberResponse { Success = true };
    }

    public override async Task<UpdateReviewStatusResponse> UpdateReviewStatus(UpdateReviewStatusRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);
        var imagePath = Path.Combine(directory, request.ImageFileName);
        var sidecarPath = SidecarStore.GetSidecarPath(imagePath);

        var sidecar = File.Exists(sidecarPath)
            ? await sidecarCache.GetAsync(sidecarPath, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" }
            : new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { ReviewStatus = NormalizeNullable(request.ReviewStatus) };

        await sidecarCache.SetAsync(sidecarPath, sidecar, cancellationToken);

        return new UpdateReviewStatusResponse { Success = true };
    }

    public override async Task<MigrateSidecarsResponse> MigrateSidecars(MigrateSidecarsRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var directory = ResolveDirectory(request.HasCardPhotosDirectory ? request.CardPhotosDirectory : null);

        var response = new MigrateSidecarsResponse();
        if (!Directory.Exists(directory))
        {
            return response;
        }

        var imageFiles = Directory
            .EnumerateFiles(directory)
            .Where(path => ScannerConfig.SupportedExtensions.Contains(Path.GetExtension(path)));

        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sidecarPath = SidecarStore.GetSidecarPath(imagePath);
            if (!File.Exists(sidecarPath))
            {
                continue;
            }

            response.TotalFiles++;

            try
            {
                var json = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
                using var document = JsonDocument.Parse(json);
                if (HasCurrentAnalysisStatusKey(document.RootElement))
                {
                    response.AlreadyCurrent++;
                    continue;
                }

                var record = await sidecarCache.GetAsync(sidecarPath, cancellationToken);
                if (record is null)
                {
                    response.AlreadyCurrent++;
                    continue;
                }

                await sidecarCache.SetAsync(sidecarPath, record, cancellationToken);
                response.Migrated++;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sidecar-Migration fuer {SidecarPath} fehlgeschlagen", sidecarPath);
                response.Errors++;
            }
        }

        return response;
    }

    private static bool HasCurrentAnalysisStatusKey(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "AnalysisStatus", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return true;
            }
        }

        return false;
    }

    public override async Task<UploadPhotoResponse> UploadPhoto(IAsyncStreamReader<UploadPhotoChunk> requestStream, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        string? originalFileName = null;
        string? cardPhotosDirectoryOverride = null;
        using var buffer = new MemoryStream();

        await foreach (var chunk in requestStream.ReadAllAsync(cancellationToken))
        {
            switch (chunk.PayloadCase)
            {
                case UploadPhotoChunk.PayloadOneofCase.Metadata:
                    originalFileName = chunk.Metadata.OriginalFileName;
                    cardPhotosDirectoryOverride = chunk.Metadata.HasCardPhotosDirectory ? chunk.Metadata.CardPhotosDirectory : null;
                    break;
                case UploadPhotoChunk.PayloadOneofCase.ChunkData:
                    var span = chunk.ChunkData.Span;
                    buffer.Write(span);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Kein Dateiname angegeben."));
        }

        if (buffer.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Die hochgeladene Datei ist leer."));
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!ScannerConfig.SupportedExtensions.Contains(extension))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP."));
        }

        var directory = ResolveDirectory(cardPhotosDirectoryOverride);
        Directory.CreateDirectory(directory);

        var fileNameStem = BuildUploadFileNameStem(originalFileName);
        var bytes = buffer.ToArray();

        for (var attempt = 0; attempt < MaxUploadFileNameAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateName = attempt == 0
                ? $"{fileNameStem}{extension}"
                : $"{fileNameStem}-{attempt}{extension}";
            var destinationPath = Path.Combine(directory, candidateName);

            try
            {
                await using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                await destinationStream.WriteAsync(bytes, cancellationToken);

                return new UploadPhotoResponse { ImageFileName = candidateName };
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                continue;
            }
        }

        throw new RpcException(new Status(StatusCode.Internal, "Es konnte kein eindeutiger Dateiname fuer den Upload erstellt werden."));
    }

    private string ResolveDirectory(string? overrideValue)
    {
        return ScannerConfig.ResolveCardPhotosDirectory(overrideValue, configuration);
    }

    private static CardEntry ToCardEntry(string imageFileName, SidecarRecord? sidecar)
    {
        var entry = new CardEntry
        {
            ImageFileName = imageFileName,
            AnalysisStatus = sidecar?.AnalysisStatus ?? "unknown",
            CardName = sidecar?.CardName ?? string.Empty,
            CardNumber = sidecar?.CardNumber ?? string.Empty,
            SetName = sidecar?.SetName ?? string.Empty,
            Rarity = sidecar?.Rarity ?? string.Empty,
            Language = sidecar?.Language ?? Languages.Default,
            Confidence = sidecar?.Confidence ?? 0,
            ReasoningSummary = sidecar?.ReasoningSummary ?? string.Empty,
            ScannedAtUtc = sidecar?.ScannedAtUtc?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
            ErrorMessage = sidecar?.ErrorMessage ?? string.Empty,
            ReviewStatus = sidecar?.ReviewStatus ?? ReviewStatuses.Unreviewed
        };
        entry.DetectedText.AddRange(sidecar?.DetectedText ?? Array.Empty<string>());
        return entry;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildUploadFileNameStem(string originalName)
    {
        var rawName = Path.GetFileNameWithoutExtension(originalName);
        var sanitizedName = UnsafeFileNameRegex.Replace(rawName.Trim(), "-").Trim('-');
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = "mobile-photo";
        }

        return $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{sanitizedName}";
    }
}
