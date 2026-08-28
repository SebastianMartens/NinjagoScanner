using System.Globalization;
using Google.Protobuf;
using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// gRPC client for PictureService only - photo upload/CRUD, sidecar updates, and download URLs.
/// Never touches catalog data; see CollectionQueryService for anything that combines this data
/// with the catalog.
/// </summary>
internal sealed class PictureServiceClient
{
    private readonly string catalogServiceAddress;
    private readonly long maxUploadBytes;
    private readonly GrpcChannel channel;

    public PictureServiceClient(string pictureServiceAddress, string catalogServiceAddress, long maxUploadBytes)
    {
        this.catalogServiceAddress = catalogServiceAddress;
        this.maxUploadBytes = maxUploadBytes;
        channel = GrpcChannel.ForAddress(pictureServiceAddress, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            },
            // ListCards now embeds a presigned S3 download_url (a few hundred bytes) on every
            // entry (see inline-photo-download-urls), so the response grows with the photo count
            // instead of staying flat - past a few thousand photos it exceeds the client's
            // default 4 MB receive limit and ListCards fails with RESOURCE_EXHAUSTED. The server
            // already sends without a size limit (MaxSendMessageSize defaults to unlimited), so
            // removing the limit here just matches that.
            MaxReceiveMessageSize = null
        });
    }

    public long MaxUploadBytes => maxUploadBytes;

    private void EnsureUploadIsValid(string fileName, long fileSizeBytes)
    {
        if (fileSizeBytes <= 0)
        {
            throw new InvalidOperationException("Die ausgewaehlte Datei ist leer.");
        }

        if (fileSizeBytes > maxUploadBytes)
        {
            throw new InvalidOperationException($"Die Datei ist zu gross. Erlaubt sind maximal {maxUploadBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!WebConfig.SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP.");
        }
    }

    public async Task<ScanSummaryDto> ScanAsync(CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var response = await client.ScanAsync(
            new ScanRequest { CatalogServiceAddress = catalogServiceAddress },
            cancellationToken: cancellationToken);

        return new ScanSummaryDto
        {
            TotalImages = response.TotalImages,
            Processed = response.Processed,
            Skipped = response.Skipped,
            Uncertain = response.Uncertain,
            Failed = response.Failed,
            HasConfigurationError = response.HasConfigurationError,
            StoppedEarly = response.StoppedEarly,
            Message = string.IsNullOrWhiteSpace(response.Message) ? null : response.Message
        };
    }

    /// <summary>
    /// Streams a photo's bytes to PictureService's client-streaming UploadPhoto RPC (metadata
    /// message, then byte-chunk messages) and returns the resulting analyzed card. Validates
    /// file type/size up front so an invalid upload never starts streaming.
    /// </summary>
    public async Task<CardListItem> UploadPhotoAsync(
        string sourceFileName,
        long fileSizeBytes,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        EnsureUploadIsValid(sourceFileName, fileSizeBytes);

        var client = new CardPictureService.CardPictureServiceClient(channel);
        using var call = client.UploadPhoto(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(new UploadPhotoRequest
        {
            Metadata = new UploadPhotoMetadata { SourceFileName = sourceFileName }
        });

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await call.RequestStream.WriteAsync(new UploadPhotoRequest
            {
                Chunk = ByteString.CopyFrom(buffer, 0, bytesRead)
            });
        }

        await call.RequestStream.CompleteAsync();
        var response = await call;

        var downloadUrl = await GetDownloadUrlAsync(response.Card.PhotoId, cancellationToken);
        response.Card.DownloadUrl = downloadUrl;
        return ToCardListItem(response.Card);
    }

    public async Task<IReadOnlyList<CardListItem>> GetCardsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await ListCardEntriesAsync(cancellationToken);
        return entries.Select(ToCardListItem).ToArray();
    }

    public async Task<IReadOnlyList<CardEntry>> ListCardEntriesAsync(CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var response = await client.ListCardsAsync(new ListCardsRequest(), cancellationToken: cancellationToken);

        return response.Cards;
    }

    public async Task<string> GetDownloadUrlAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var response = await client.GetPhotoDownloadUrlAsync(
            new GetPhotoDownloadUrlRequest { PhotoId = photoId },
            cancellationToken: cancellationToken);

        return response.DownloadUrl;
    }

    /// <summary>
    /// Resolves the sidecar fields not carried on <see cref="CardListItem"/> (confidence,
    /// reasoning, detected text, scanned-at timestamp, error message) for a single photo, for
    /// callers showing one card's full details on demand rather than every ListCards row.
    /// </summary>
    public async Task<CardDetailsItem> GetCardDetailsAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var response = await client.GetCardDetailsAsync(
            new GetCardDetailsRequest { PhotoId = photoId },
            cancellationToken: cancellationToken);

        return ToCardDetailsItem(response.Details);
    }

    public async Task UpdateCardSidecarAsync(
        string photoId,
        CollectionCardSidecarUpdate update,
        CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new UpdateSidecarRequest
        {
            PhotoId = photoId,
            AnalysisStatus = update.AnalysisStatus ?? string.Empty,
            CardName = update.CardName ?? string.Empty,
            CardNumber = update.CardNumber ?? string.Empty,
            SetName = update.SetName ?? string.Empty,
            Rarity = update.Rarity ?? string.Empty,
            Language = update.Language ?? string.Empty,
            Confidence = update.Confidence,
            ReasoningSummary = update.ReasoningSummary ?? string.Empty,
            ErrorMessage = update.ErrorMessage ?? string.Empty,
            ReviewStatus = update.ReviewStatus ?? string.Empty
        };
        request.DetectedText.AddRange(update.DetectedText.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()));

        await client.UpdateSidecarAsync(request, cancellationToken: cancellationToken);
    }

    public async Task UpdateReviewStatusAsync(string photoId, string reviewStatus, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateReviewStatusAsync(
            new UpdateReviewStatusRequest { PhotoId = photoId, ReviewStatus = reviewStatus },
            cancellationToken: cancellationToken);
    }

    public async Task DeletePhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.DeletePhotoAsync(new DeletePhotoRequest { PhotoId = photoId }, cancellationToken: cancellationToken);
    }

    public async Task UpdateSetNameAsync(string photoId, string? setName, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateSetNameAsync(
            new UpdateSetNameRequest { PhotoId = photoId, SetName = string.IsNullOrWhiteSpace(setName) ? string.Empty : setName.Trim() },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardNumberAsync(string photoId, string? cardNumber, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateCardNumberAsync(
            new UpdateCardNumberRequest { PhotoId = photoId, CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? string.Empty : cardNumber.Trim() },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardLanguageAsync(string photoId, string? language, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateCardLanguageAsync(
            new UpdateCardLanguageRequest { PhotoId = photoId, Language = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim() },
            cancellationToken: cancellationToken);
    }

    private static CardListItem ToCardListItem(CardEntry entry)
    {
        return new CardListItem
        {
            PhotoId = entry.PhotoId,
            SourceFileName = string.IsNullOrWhiteSpace(entry.SourceFileName) ? entry.PhotoId : entry.SourceFileName,
            ImageUrl = entry.DownloadUrl,
            AnalysisStatus = entry.AnalysisStatus,
            CardName = NormalizeNullable(entry.CardName),
            CardNumber = NormalizeNullable(entry.CardNumber),
            SetName = NormalizeNullable(entry.SetName),
            Rarity = NormalizeNullable(entry.Rarity),
            Language = NormalizeNullable(entry.Language) ?? Languages.Default,
            ReviewStatus = NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed
        };
    }

    private static CardDetailsItem ToCardDetailsItem(CardDetails details)
    {
        return new CardDetailsItem
        {
            Confidence = details.Confidence,
            ReasoningSummary = NormalizeNullable(details.ReasoningSummary),
            DetectedText = details.DetectedText.ToArray(),
            ScannedAtUtc = ParseScannedAtUtc(details.ScannedAtUtc),
            ErrorMessage = NormalizeNullable(details.ErrorMessage)
        };
    }

    private static DateTimeOffset? ParseScannedAtUtc(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
