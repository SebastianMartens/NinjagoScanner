using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NinjagoScanner.PictureService.Protos;
using System.Globalization;

namespace NinjagoScanner.PictureService.Services;

public sealed class PictureScannerGrpcService : CardPictureService.CardPictureServiceBase
{
    private readonly IConfiguration configuration;
    private readonly ILogger<PictureScannerGrpcService> logger;
    private readonly SidecarCache sidecarCache;
    private readonly IPhotoStore photoStore;

    internal PictureScannerGrpcService(IConfiguration configuration, ILogger<PictureScannerGrpcService> logger, SidecarCache sidecarCache, IPhotoStore photoStore)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.sidecarCache = sidecarCache;
        this.photoStore = photoStore;
    }

    /// <summary>
    /// Bulk backfill: analyzes every photo in S3 that has no sidecar record yet (or all of them,
    /// with OverwriteExistingSidecars). Individual uploads are normally analyzed one at a time via
    /// <see cref="AnalyzePhoto"/> right after the browser finishes its direct-to-S3 upload, so this
    /// is mainly an admin/recovery operation.
    /// </summary>
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

        var photoIds = new List<string>();
        await foreach (var photoId in photoStore.ListPhotoIdsAsync(cancellationToken))
        {
            photoIds.Add(photoId);
        }
        photoIds.Sort(StringComparer.Ordinal);

        if (photoIds.Count == 0)
        {
            return new ScanSummary
            {
                TotalImages = 0,
                Message = "Im Foto-Bucket wurden keine Kartenbilder gefunden."
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

        for (var index = 0; index < photoIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var photoId = photoIds[index];
            var existing = await sidecarCache.GetAsync(photoId, cancellationToken);

            if (!config.OverwriteExistingSidecars && existing is not null)
            {
                skippedCount++;
                continue;
            }

            var sourceFileName = existing?.SourceFileName ?? photoId;

            CardAnalysisResult result;
            try
            {
                var imageBytes = await photoStore.GetBytesAsync(photoId, cancellationToken);
                result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, seriesCatalog, photoId, sourceFileName, imageBytes, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unerwarteter Fehler bei der Analyse von {PhotoId}", photoId);
                result = new CardAnalysisResult
                {
                    PhotoId = photoId,
                    AnalysisStatus = AnalysisStatuses.Failed,
                    SourceFileName = sourceFileName,
                    AiModel = config.Model,
                    ScannedAtUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = $"Unerwarteter Fehler: {exception.Message}",
                    DetectedText = Array.Empty<string>()
                };
            }

            if (!string.IsNullOrWhiteSpace(existing?.ReviewStatus))
            {
                result = result with { ReviewStatus = existing.ReviewStatus };
            }

            await sidecarCache.SetAsync(photoId, result, cancellationToken);

            logger.LogDebug(
                "[{Index}/{Total}] {PhotoId} → Status: {AnalysisStatus} | Karte: {CardName} | Serie: {SetName} | Nr: {CardNumber}",
                index + 1,
                photoIds.Count,
                photoId,
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

            if (index < photoIds.Count - 1 && config.DelayBetweenRequestsMs > 0)
            {
                await Task.Delay(config.DelayBetweenRequestsMs, cancellationToken);
            }
        }

        return new ScanSummary
        {
            TotalImages = photoIds.Count,
            Processed = processedCount,
            Skipped = skippedCount,
            Uncertain = uncertainCount,
            Failed = failedCount,
            Message = "Batch abgeschlossen."
        };
    }

    /// <summary>
    /// Client-streaming upload: reads the metadata message and every following byte-chunk message
    /// from the caller (the Web app, forwarding bytes it received from the browser), stores the
    /// reconstructed file under a generated photo ID, and runs AI Analysis on it before returning.
    /// </summary>
    public override async Task<UploadPhotoResponse> UploadPhoto(IAsyncStreamReader<UploadPhotoRequest> requestStream, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        if (!await requestStream.MoveNext(cancellationToken)
            || requestStream.Current.PayloadCase != UploadPhotoRequest.PayloadOneofCase.Metadata)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Der Upload-Stream muss mit einer Metadaten-Nachricht beginnen."));
        }

        var metadata = requestStream.Current.Metadata;
        var sourceFileName = string.IsNullOrWhiteSpace(metadata.SourceFileName) ? "upload" : metadata.SourceFileName;

        var extension = Path.GetExtension(sourceFileName).ToLowerInvariant();
        if (!ScannerConfig.SupportedExtensions.Contains(extension))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP."));
        }

        using var buffer = new MemoryStream();
        while (await requestStream.MoveNext(cancellationToken))
        {
            if (requestStream.Current.PayloadCase != UploadPhotoRequest.PayloadOneofCase.Chunk)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Nach der Metadaten-Nachricht werden nur Byte-Chunks erwartet."));
            }

            buffer.Write(requestStream.Current.Chunk.Span);
        }

        if (buffer.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Die hochgeladene Datei ist leer."));
        }

        var photoId = Guid.NewGuid().ToString("n");
        var imageBytes = buffer.ToArray();
        await photoStore.PutBytesAsync(photoId, imageBytes, cancellationToken);

        var appConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddUserSecrets<PictureScannerGrpcService>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var config = ScannerConfig.Load(appConfiguration, metadata);

        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "GEMINI_API_KEY ist nicht gesetzt."));
        }

        IReadOnlyList<SeriesInfo> seriesCatalog;
        try
        {
            seriesCatalog = await CatalogGrpcClient.LoadSeriesCatalogAsync(config.CatalogServiceAddress, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Katalog-Service unter {CatalogServiceAddress} nicht erreichbar", config.CatalogServiceAddress);
            throw new RpcException(new Status(StatusCode.Unavailable, $"Der CatalogService unter '{config.CatalogServiceAddress}' ist nicht erreichbar."));
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };

        CardAnalysisResult result;
        try
        {
            result = await GeminiApiService.AnalyzeCardAsync(httpClient, config, seriesCatalog, photoId, sourceFileName, imageBytes, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unerwarteter Fehler bei der Analyse von {PhotoId}", photoId);
            result = new CardAnalysisResult
            {
                PhotoId = photoId,
                AnalysisStatus = AnalysisStatuses.Failed,
                SourceFileName = sourceFileName,
                AiModel = config.Model,
                ScannedAtUtc = DateTimeOffset.UtcNow,
                ErrorMessage = $"Unerwarteter Fehler: {exception.Message}",
                DetectedText = Array.Empty<string>()
            };
        }

        await sidecarCache.SetAsync(photoId, result, cancellationToken);

        return new UploadPhotoResponse
        {
            Card = ToCardEntry(photoId, await sidecarCache.GetAsync(photoId, cancellationToken))
        };
    }

    /// <summary>
    /// Issues a short-lived pre-signed S3 GET URL for a stored photo, so the browser can fetch
    /// image bytes directly from S3 without this service (or its caller) proxying them.
    /// </summary>
    public override async Task<GetPhotoDownloadUrlResponse> GetPhotoDownloadUrl(GetPhotoDownloadUrlRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        if (string.IsNullOrWhiteSpace(request.PhotoId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Keine photo_id angegeben."));
        }

        if (!await photoStore.ExistsAsync(request.PhotoId, cancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Foto '{request.PhotoId}' wurde im Speicher nicht gefunden."));
        }

        var downloadUrl = await photoStore.CreateDownloadUrlAsync(request.PhotoId, cancellationToken);
        return new GetPhotoDownloadUrlResponse { DownloadUrl = downloadUrl };
    }

    public override async Task<ListCardsResponse> ListCards(ListCardsRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var response = new ListCardsResponse();

        await foreach (var photoId in photoStore.ListPhotoIdsAsync(cancellationToken))
        {
            var record = await sidecarCache.GetAsync(photoId, cancellationToken);
            response.Cards.Add(ToCardEntry(photoId, record));
        }

        return response;
    }

    public override async Task<UpdateSidecarResponse> UpdateSidecar(UpdateSidecarRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var existing = await sidecarCache.GetAsync(request.PhotoId, cancellationToken) ?? new SidecarRecord();

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
            ReviewStatus = NormalizeNullable(request.ReviewStatus)
        };

        await sidecarCache.SetAsync(request.PhotoId, updated, cancellationToken);

        return new UpdateSidecarResponse { Success = true };
    }

    public override async Task<UpdateSetNameResponse> UpdateSetName(UpdateSetNameRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var sidecar = await sidecarCache.GetAsync(request.PhotoId, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { SetName = NormalizeNullable(request.SetName) };

        await sidecarCache.SetAsync(request.PhotoId, sidecar, cancellationToken);

        return new UpdateSetNameResponse { Success = true };
    }

    public override async Task<UpdateCardNumberResponse> UpdateCardNumber(UpdateCardNumberRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var sidecar = await sidecarCache.GetAsync(request.PhotoId, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { CardNumber = NormalizeNullable(request.CardNumber) };

        await sidecarCache.SetAsync(request.PhotoId, sidecar, cancellationToken);

        return new UpdateCardNumberResponse { Success = true };
    }

    public override async Task<UpdateCardLanguageResponse> UpdateCardLanguage(UpdateCardLanguageRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var sidecar = await sidecarCache.GetAsync(request.PhotoId, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { Language = NormalizeNullable(request.Language) };

        await sidecarCache.SetAsync(request.PhotoId, sidecar, cancellationToken);

        return new UpdateCardLanguageResponse { Success = true };
    }

    public override async Task<UpdateReviewStatusResponse> UpdateReviewStatus(UpdateReviewStatusRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var sidecar = await sidecarCache.GetAsync(request.PhotoId, cancellationToken) ?? new SidecarRecord { AnalysisStatus = "pending" };

        sidecar = sidecar with { ReviewStatus = NormalizeNullable(request.ReviewStatus) };

        await sidecarCache.SetAsync(request.PhotoId, sidecar, cancellationToken);

        return new UpdateReviewStatusResponse { Success = true };
    }

    /// <summary>
    /// Safety net for sidecar records written in an older shape (missing AnalysisStatus). Every
    /// record this service writes going forward always has AnalysisStatus set, so this is
    /// expected to be a no-op except right after the one-time cardFotos/ migration.
    /// </summary>
    public override async Task<MigrateSidecarsResponse> MigrateSidecars(MigrateSidecarsRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;
        var response = new MigrateSidecarsResponse();

        await foreach (var (photoId, record) in sidecarCache.ListAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            response.TotalFiles++;

            if (!string.IsNullOrWhiteSpace(record.AnalysisStatus))
            {
                response.AlreadyCurrent++;
                continue;
            }

            try
            {
                var repaired = record with
                {
                    AnalysisStatus = AnalysisStatuses.Failed,
                    ErrorMessage = record.ErrorMessage ?? "Sidecar-Datensatz wurde ohne AnalysisStatus migriert."
                };
                await sidecarCache.SetAsync(photoId, repaired, cancellationToken);
                response.Migrated++;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sidecar-Migration fuer {PhotoId} fehlgeschlagen", photoId);
                response.Errors++;
            }
        }

        return response;
    }

    public override async Task<DeletePhotoResponse> DeletePhoto(DeletePhotoRequest request, ServerCallContext context)
    {
        var cancellationToken = context.CancellationToken;

        if (!await photoStore.ExistsAsync(request.PhotoId, cancellationToken))
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Das Foto '{request.PhotoId}' wurde nicht gefunden."));
        }

        await photoStore.DeleteAsync(request.PhotoId, cancellationToken);
        await sidecarCache.RemoveAsync(request.PhotoId, cancellationToken);

        return new DeletePhotoResponse { Success = true };
    }

    private static CardEntry ToCardEntry(string photoId, SidecarRecord? sidecar)
    {
        var entry = new CardEntry
        {
            PhotoId = photoId,
            SourceFileName = sidecar?.SourceFileName ?? string.Empty,
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
}
