using System.Globalization;
using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components.Authorization;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Data;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// gRPC client for PictureService only - photo upload/CRUD, sidecar updates, and download URLs.
/// Never touches catalog data; see CollectionQueryService for anything that combines this data
/// with the catalog. Scoped (one instance per Blazor circuit, see collection-scoped-picture-access)
/// so it can resolve the acting user's collection_id once and attach it to every outgoing
/// request - callers never pass a collection_id themselves.
/// </summary>
internal sealed class PictureServiceClient
{
    private readonly string catalogServiceAddress;
    private readonly long maxUploadBytes;
    private readonly GrpcChannel channel;
    private readonly ICurrentCollectionContext currentCollectionContext;
    private readonly AuthenticationStateProvider authenticationStateProvider;
    private string? cachedCollectionId;

    public PictureServiceClient(
        GrpcChannel channel,
        string catalogServiceAddress,
        long maxUploadBytes,
        ICurrentCollectionContext currentCollectionContext,
        AuthenticationStateProvider authenticationStateProvider)
    {
        this.channel = channel;
        this.catalogServiceAddress = catalogServiceAddress;
        this.maxUploadBytes = maxUploadBytes;
        this.currentCollectionContext = currentCollectionContext;
        this.authenticationStateProvider = authenticationStateProvider;
    }

    public long MaxUploadBytes => maxUploadBytes;

    /// <summary>
    /// Resolves and caches (for this circuit's lifetime) the collection_id every collection-scoped
    /// RPC in this class attaches to its request - see collection-scoped-picture-access's "Web
    /// resolves and supplies the caller's authorized collection_id" requirement.
    /// </summary>
    private async Task<string> GetCollectionIdAsync(CancellationToken cancellationToken)
    {
        if (cachedCollectionId is not null)
        {
            return cachedCollectionId;
        }

        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var collection = await currentCollectionContext.GetOwnedCollectionAsync(authState.User, cancellationToken);
        if (collection is null)
        {
            throw new InvalidOperationException("Der aktuelle Benutzer besitzt keine Sammlung.");
        }

        cachedCollectionId = collection.Id;
        return cachedCollectionId;
    }

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
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        var response = await client.ScanAsync(
            new ScanRequest { CatalogServiceAddress = catalogServiceAddress, CollectionId = collectionId },
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
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        using var call = client.UploadPhoto(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(new UploadPhotoRequest
        {
            Metadata = new UploadPhotoMetadata { SourceFileName = sourceFileName, CollectionId = collectionId }
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
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var response = await client.ListCardsAsync(
            new ListCardsRequest { CollectionId = collectionId },
            cancellationToken: cancellationToken);

        return response.Cards;
    }

    public async Task<string> GetDownloadUrlAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var response = await client.GetPhotoDownloadUrlAsync(
            new GetPhotoDownloadUrlRequest { PhotoId = photoId, CollectionId = collectionId },
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
        var collectionId = await GetCollectionIdAsync(cancellationToken);
        var response = await client.GetCardDetailsAsync(
            new GetCardDetailsRequest { PhotoId = photoId, CollectionId = collectionId },
            cancellationToken: cancellationToken);

        return ToCardDetailsItem(response.Details);
    }

    public async Task UpdateCardSidecarAsync(
        string photoId,
        CollectionCardSidecarUpdate update,
        CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        var request = new UpdateSidecarRequest
        {
            PhotoId = photoId,
            CollectionId = collectionId,
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
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        await client.UpdateReviewStatusAsync(
            new UpdateReviewStatusRequest { PhotoId = photoId, ReviewStatus = reviewStatus, CollectionId = collectionId },
            cancellationToken: cancellationToken);
    }

    public async Task DeletePhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        await client.DeletePhotoAsync(
            new DeletePhotoRequest { PhotoId = photoId, CollectionId = collectionId },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateSetNameAsync(string photoId, string? setName, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        await client.UpdateSetNameAsync(
            new UpdateSetNameRequest
            {
                PhotoId = photoId,
                SetName = string.IsNullOrWhiteSpace(setName) ? string.Empty : setName.Trim(),
                CollectionId = collectionId
            },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardNumberAsync(string photoId, string? cardNumber, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        await client.UpdateCardNumberAsync(
            new UpdateCardNumberRequest
            {
                PhotoId = photoId,
                CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? string.Empty : cardNumber.Trim(),
                CollectionId = collectionId
            },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardLanguageAsync(string photoId, string? language, CancellationToken cancellationToken = default)
    {
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var collectionId = await GetCollectionIdAsync(cancellationToken);

        await client.UpdateCardLanguageAsync(
            new UpdateCardLanguageRequest
            {
                PhotoId = photoId,
                Language = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim(),
                CollectionId = collectionId
            },
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
