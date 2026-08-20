using System.Globalization;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using NinjagoScanner.CatalogService.Protos;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Shared;
using NinjagoScanner.Web.Shared.Models;

namespace NinjagoScanner.Web.Bff.Services;

/// <summary>
/// Ported from the former NinjagoScanner.Web/Services/CardCatalogService.cs: merges catalog data
/// (from CatalogService) with scanned photo data (from PictureService) into the shapes the BFF's
/// HTTP/JSON endpoints return. All card photo/sidecar access is delegated to PictureService via
/// gRPC, keyed by generated photo ID rather than file name; this class never touches S3 directly
/// (see <see cref="IUploadUrlIssuer"/> for that seam).
/// </summary>
internal sealed class CardCatalogService(
    string catalogServiceAddress,
    string pictureServiceAddress,
    IUploadUrlIssuer uploadUrlIssuer,
    long maxUploadBytes)
{
    private static readonly Regex NumberOnlyRegex = new("^\\d+$", RegexOptions.Compiled);

    public long MaxUploadBytes => maxUploadBytes;

    public (string PhotoId, string ContentType) ValidateUpload(string fileName, long fileSizeBytes, string? contentType)
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
        if (!BffConfig.SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP.");
        }

        var photoId = Guid.NewGuid().ToString("n");
        return (photoId, string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
    }

    public async Task<string> CreateUploadUrlAsync(string photoId, string contentType, CancellationToken cancellationToken)
    {
        return await uploadUrlIssuer.CreateUploadUrlAsync(photoId, contentType, cancellationToken);
    }

    public async Task<CardListItem> ConfirmUploadAsync(string photoId, string sourceFileName, CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var response = await client.AnalyzePhotoAsync(
            new AnalyzePhotoRequest { PhotoId = photoId, SourceFileName = sourceFileName },
            cancellationToken: cancellationToken);

        return await ToCardListItemAsync(response.Card, cancellationToken);
    }

    public async Task<IReadOnlyList<CardListItem>> GetCardsAsync(CancellationToken cancellationToken = default)
    {
        var entries = await LoadCardEntriesAsync(cancellationToken);
        return await ToCardListItemsAsync(entries, cancellationToken);
    }

    public async Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var photoEntries = await LoadCardEntriesAsync(cancellationToken);
        var ownershipByKey = BuildOwnershipLookup(photoEntries, out var totalPhotos, out var mappedPhotos);

        var cards = cardsFromCatalog
            .Select(card =>
            {
                ownershipByKey.TryGetValue(BuildOwnershipKey(card.Series, card.CardNumber), out var ownedCopies);
                return new CollectionCardItem
                {
                    Series = card.Series,
                    SortOrder = card.SortOrder,
                    Category = card.Category,
                    CardNumber = card.CardNumber,
                    CardName = card.CardName,
                    OwnedCopies = ownedCopies
                };
            })
            .OrderBy(card => card.SortOrder)
            .ThenBy(card => CardNumberSorting.BuildSortKey(card.CardNumber), StringComparer.Ordinal)
            .ThenBy(card => card.CardName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CollectionOverviewResult
        {
            Cards = cards,
            TotalPhotos = totalPhotos,
            MappedPhotos = mappedPhotos
        };
    }

    public async Task<IReadOnlyList<GalleryCardItem>> GetGalleryCardsAsync(string series, CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var photoEntries = await LoadCardEntriesAsync(cancellationToken);
        var photosByKey = photoEntries.ToLookup(entry => BuildOwnershipKey(entry.SetName, entry.CardNumber));

        var seriesKey = NormalizeSeriesKey(series);

        var matchedCards = cardsFromCatalog
            .Where(card => string.Equals(NormalizeSeriesKey(card.Series), seriesKey, StringComparison.Ordinal))
            .Select(card =>
            {
                var ownershipKey = BuildOwnershipKey(card.Series, card.CardNumber);
                var hasOwnershipKey = !string.IsNullOrWhiteSpace(ownershipKey);
                var photoCount = hasOwnershipKey ? photosByKey[ownershipKey].Count() : 0;
                var matchedPhoto = hasOwnershipKey
                    ? photosByKey[ownershipKey]
                        .OrderBy(entry => entry.PhotoId, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault()
                    : null;

                return (Card: card, MatchedPhoto: matchedPhoto, PhotoCount: photoCount);
            })
            .OrderBy(item => item.Card.SortOrder)
            .ThenBy(item => CardNumberSorting.BuildSortKey(item.Card.CardNumber), StringComparer.Ordinal)
            .ThenBy(item => item.Card.CardName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<GalleryCardItem>(matchedCards.Length);
        foreach (var (card, matchedPhoto, photoCount) in matchedCards)
        {
            result.Add(new GalleryCardItem
            {
                Series = card.Series,
                SortOrder = card.SortOrder,
                Category = card.Category,
                CardNumber = card.CardNumber,
                CardName = card.CardName,
                PhotoId = matchedPhoto?.PhotoId,
                ImageUrl = matchedPhoto is null ? null : await uploadUrlIssuer.CreateDownloadUrlAsync(matchedPhoto.PhotoId, cancellationToken),
                PhotoCount = photoCount,
                Rarity = matchedPhoto?.Rarity,
                ReviewStatus = matchedPhoto is null ? null : NormalizeNullable(matchedPhoto.ReviewStatus)
            });
        }

        return result;
    }

    public async Task<SeriesSummaryResult> GetSeriesSummaryAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var photoEntries = await LoadCardEntriesAsync(cancellationToken);

        var seriesGroups = cardsFromCatalog
            .GroupBy(card => card.Series, StringComparer.Ordinal)
            .Select(group => new
            {
                SeriesName = group.Key,
                SortOrder = group.Min(card => card.SortOrder),
                TotalCards = group.Count(),
                CardNumbers = group.Select(card => card.CardNumber).ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .ToArray();

        var photosBySeriesKey = photoEntries.ToLookup(entry => NormalizeSeriesNameForSummary(entry.SetName));

        var seriesItems = seriesGroups
            .Select(series =>
            {
                var photosForSeries = photosBySeriesKey[NormalizeSeriesNameForSummary(series.SeriesName)];
                var ownedCards = photosForSeries
                    .Select(entry => NormalizeCardNumber(entry.CardNumber))
                    .Where(number => series.CardNumbers.Contains(number))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();

                return new SeriesSummaryItem
                {
                    SeriesName = series.SeriesName,
                    SortOrder = series.SortOrder,
                    TotalCards = series.TotalCards,
                    OwnedCards = ownedCards,
                    TotalPhotos = photosForSeries.Count()
                };
            })
            .OrderBy(item => item.SortOrder)
            .ToArray();

        var knownSeriesKeys = seriesGroups
            .Select(series => NormalizeSeriesNameForSummary(series.SeriesName))
            .ToHashSet(StringComparer.Ordinal);
        var unknownSeriesPhotoCount = photoEntries.Count(entry => !knownSeriesKeys.Contains(NormalizeSeriesNameForSummary(entry.SetName)));

        return new SeriesSummaryResult
        {
            Series = seriesItems,
            UnknownSeriesPhotoCount = unknownSeriesPhotoCount,
            TotalCatalogCards = cardsFromCatalog.Count,
            OwnedCatalogCards = seriesItems.Sum(item => item.OwnedCards),
            TotalPhotos = photoEntries.Count,
            AnalysisStatusCounts = BuildAnalysisStatusCounts(photoEntries),
            ReviewStatusCounts = BuildReviewStatusCounts(photoEntries)
        };
    }

    private static PhotoAnalysisStatusCounts BuildAnalysisStatusCounts(IReadOnlyList<CardEntry> entries)
    {
        return new PhotoAnalysisStatusCounts
        {
            Ok = entries.Count(entry => string.Equals(entry.AnalysisStatus, AnalysisStatuses.Ok, StringComparison.OrdinalIgnoreCase)),
            Uncertain = entries.Count(entry => string.Equals(entry.AnalysisStatus, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase)),
            Failed = entries.Count(entry => string.Equals(entry.AnalysisStatus, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase)),
            NotAnalyzed = entries.Count(entry =>
                !string.Equals(entry.AnalysisStatus, AnalysisStatuses.Ok, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.AnalysisStatus, AnalysisStatuses.Uncertain, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry.AnalysisStatus, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static PhotoReviewStatusCounts BuildReviewStatusCounts(IReadOnlyList<CardEntry> entries)
    {
        return new PhotoReviewStatusCounts
        {
            Unreviewed = entries.Count(entry => (NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed) == ReviewStatuses.Unreviewed),
            Verified = entries.Count(entry => NormalizeNullable(entry.ReviewStatus) == ReviewStatuses.Verified),
            Incorrect = entries.Count(entry => NormalizeNullable(entry.ReviewStatus) == ReviewStatuses.Incorrect)
        };
    }

    public async Task<CollectionCardDetails?> GetCollectionCardDetailsAsync(
        string series,
        string cardNumber,
        CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var card = cardsFromCatalog.FirstOrDefault(entry =>
            string.Equals(NormalizeSeriesKey(entry.Series), NormalizeSeriesKey(series), StringComparison.Ordinal)
            && string.Equals(entry.CardNumber, NormalizeCardNumber(cardNumber), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(card.Series))
        {
            return null;
        }

        var metadata = await LoadSeriesMetadataAsync(series, cancellationToken);
        var photoEntries = await LoadCardEntriesAsync(cancellationToken);
        var photos = await BuildCardPhotosAsync(photoEntries, series, cardNumber, cancellationToken);

        return new CollectionCardDetails
        {
            Series = card.Series,
            SortOrder = card.SortOrder,
            Category = card.Category,
            CardNumber = card.CardNumber,
            CardName = card.CardName,
            Year = metadata.Year,
            Logo = metadata.Logo,
            Theme = metadata.Theme,
            Highlights = metadata.Highlights,
            Photos = photos
        };
    }

    public async Task UpdateCardSidecarAsync(
        string photoId,
        CollectionCardSidecarUpdate update,
        CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
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
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateReviewStatusAsync(
            new UpdateReviewStatusRequest { PhotoId = photoId, ReviewStatus = reviewStatus },
            cancellationToken: cancellationToken);
    }

    public async Task DeletePhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.DeletePhotoAsync(new DeletePhotoRequest { PhotoId = photoId }, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<CardReviewGroup>> GetReviewGroupsAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var catalogByKey = new Dictionary<string, (string SeriesName, string CardNumber, string CardName, int SortOrder)>(StringComparer.Ordinal);
        foreach (var card in cardsFromCatalog)
        {
            var key = BuildOwnershipKey(card.Series, card.CardNumber);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            catalogByKey.TryAdd(key, (card.Series, card.CardNumber, card.CardName, card.SortOrder));
        }

        var entries = await LoadCardEntriesAsync(cancellationToken);
        var photos = await ToCardListItemsAsync(entries, cancellationToken);

        var catalogGroups = new Dictionary<string, (string SeriesName, string CardNumber, string CardName, int SortOrder, List<CardListItem> Photos)>(StringComparer.Ordinal);
        var catchAll = new List<CardListItem>();

        foreach (var photo in photos)
        {
            var key = BuildOwnershipKey(photo.SetName, photo.CardNumber);
            if (!string.IsNullOrWhiteSpace(key) && catalogByKey.TryGetValue(key, out var catalogCard))
            {
                if (!catalogGroups.TryGetValue(key, out var group))
                {
                    group = (catalogCard.SeriesName, catalogCard.CardNumber, catalogCard.CardName, catalogCard.SortOrder, new List<CardListItem>());
                    catalogGroups[key] = group;
                }

                group.Photos.Add(photo);
            }
            else
            {
                catchAll.Add(photo);
            }
        }

        var groups = catalogGroups.Values
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => CardNumberSorting.BuildSortKey(group.CardNumber), StringComparer.Ordinal)
            .Select(group => new CardReviewGroup
            {
                IsCatchAll = false,
                SeriesName = group.SeriesName,
                CardNumber = group.CardNumber,
                CardName = group.CardName,
                Photos = group.Photos.OrderBy(photo => photo.PhotoId, StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .ToList();

        if (catchAll.Count > 0)
        {
            groups.Add(new CardReviewGroup
            {
                IsCatchAll = true,
                Photos = catchAll.OrderBy(photo => photo.PhotoId, StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        return groups;
    }

    public async Task<IReadOnlyList<string>> GetKnownSeriesAsync(CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(catalogServiceAddress);
        var client = new CardCatalog.CardCatalogClient(channel);
        var response = await client.ListSeriesAsync(
            new ListSeriesRequest { IncludeKnownCardNames = false },
            cancellationToken: cancellationToken);

        return response.Series
            .Select(entry => entry.SeriesName.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task UpdateSetNameAsync(string photoId, string? setName, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateSetNameAsync(
            new UpdateSetNameRequest { PhotoId = photoId, SetName = string.IsNullOrWhiteSpace(setName) ? string.Empty : setName.Trim() },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardNumberAsync(string photoId, string? cardNumber, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateCardNumberAsync(
            new UpdateCardNumberRequest { PhotoId = photoId, CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? string.Empty : cardNumber.Trim() },
            cancellationToken: cancellationToken);
    }

    public async Task UpdateCardLanguageAsync(string photoId, string? language, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        await client.UpdateCardLanguageAsync(
            new UpdateCardLanguageRequest { PhotoId = photoId, Language = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim() },
            cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyList<CardEntry>> LoadCardEntriesAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var response = await client.ListCardsAsync(new ListCardsRequest(), cancellationToken: cancellationToken);

        return response.Cards;
    }

    private async Task<IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)>> LoadCardsFromCatalogServiceAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(catalogServiceAddress);
        var client = new CardCatalog.CardCatalogClient(channel);
        var response = await client.ListAllCardsAsync(new Empty(), cancellationToken: cancellationToken);

        return response.Cards
            .Select(card =>
            {
                var normalizedNumber = NormalizeCardNumber(card.CardNumber);
                return (
                    Series: card.SeriesName?.Trim() ?? string.Empty,
                    Category: string.IsNullOrWhiteSpace(card.Category) ? "Unkategorisiert" : card.Category.Trim(),
                    CardNumber: normalizedNumber,
                    CardName: card.CardName?.Trim() ?? string.Empty,
                    SortOrder: card.SortOrder
                );
            })
            .Where(card =>
                !string.IsNullOrWhiteSpace(card.Series)
                && !string.IsNullOrWhiteSpace(card.CardNumber)
                && !string.IsNullOrWhiteSpace(card.CardName))
            .ToArray();
    }

    private async Task<SeriesMetadata> LoadSeriesMetadataAsync(string series, CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(catalogServiceAddress);
        var client = new CardCatalog.CardCatalogClient(channel);
        var response = await client.GetSeriesMetadataAsync(
            new GetSeriesMetadataRequest { SeriesName = series },
            cancellationToken: cancellationToken);

        if (!response.Found || response.Metadata is null)
        {
            return new SeriesMetadata();
        }

        return new SeriesMetadata
        {
            Year = response.Metadata.Year > 0 ? response.Metadata.Year : null,
            Logo = string.IsNullOrWhiteSpace(response.Metadata.Logo) ? null : response.Metadata.Logo,
            Theme = string.IsNullOrWhiteSpace(response.Metadata.Theme) ? null : response.Metadata.Theme,
            Highlights = response.Metadata.Highlights
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text.Trim())
                .ToArray()
        };
    }

    private async Task<CardListItem> ToCardListItemAsync(CardEntry entry, CancellationToken cancellationToken)
    {
        return new CardListItem
        {
            PhotoId = entry.PhotoId,
            SourceFileName = string.IsNullOrWhiteSpace(entry.SourceFileName) ? entry.PhotoId : entry.SourceFileName,
            ImageUrl = await uploadUrlIssuer.CreateDownloadUrlAsync(entry.PhotoId, cancellationToken),
            AnalysisStatus = entry.AnalysisStatus,
            CardName = NormalizeNullable(entry.CardName),
            CardNumber = NormalizeNullable(entry.CardNumber),
            SetName = NormalizeNullable(entry.SetName),
            Rarity = NormalizeNullable(entry.Rarity),
            Language = NormalizeNullable(entry.Language) ?? Languages.Default,
            Confidence = entry.Confidence,
            ReasoningSummary = NormalizeNullable(entry.ReasoningSummary),
            DetectedText = entry.DetectedText.ToArray(),
            ScannedAtUtc = ParseScannedAtUtc(entry.ScannedAtUtc),
            ErrorMessage = NormalizeNullable(entry.ErrorMessage),
            ReviewStatus = NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed
        };
    }

    private async Task<IReadOnlyList<CardListItem>> ToCardListItemsAsync(IReadOnlyList<CardEntry> entries, CancellationToken cancellationToken)
    {
        var result = new List<CardListItem>(entries.Count);
        foreach (var entry in entries)
        {
            result.Add(await ToCardListItemAsync(entry, cancellationToken));
        }

        return result;
    }

    private static CollectionCardSidecarData ToCollectionSidecar(CardEntry entry)
    {
        return new CollectionCardSidecarData
        {
            AnalysisStatus = entry.AnalysisStatus,
            CardName = NormalizeNullable(entry.CardName),
            CardNumber = NormalizeNullable(entry.CardNumber),
            SetName = NormalizeNullable(entry.SetName),
            Rarity = NormalizeNullable(entry.Rarity),
            Language = NormalizeNullable(entry.Language) ?? Languages.Default,
            Confidence = entry.Confidence,
            ReasoningSummary = NormalizeNullable(entry.ReasoningSummary),
            DetectedText = entry.DetectedText.ToArray(),
            ScannedAtUtc = ParseScannedAtUtc(entry.ScannedAtUtc),
            ErrorMessage = NormalizeNullable(entry.ErrorMessage),
            ReviewStatus = NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed
        };
    }

    private async Task<IReadOnlyList<CollectionCardPhotoItem>> BuildCardPhotosAsync(
        IReadOnlyList<CardEntry> entries,
        string series,
        string cardNumber,
        CancellationToken cancellationToken)
    {
        var ownershipKey = BuildOwnershipKey(series, cardNumber);
        if (string.IsNullOrWhiteSpace(ownershipKey))
        {
            return Array.Empty<CollectionCardPhotoItem>();
        }

        var matches = entries
            .Where(entry => string.Equals(BuildOwnershipKey(entry.SetName, entry.CardNumber), ownershipKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.PhotoId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var result = new List<CollectionCardPhotoItem>(matches.Length);
        foreach (var entry in matches)
        {
            result.Add(new CollectionCardPhotoItem
            {
                PhotoId = entry.PhotoId,
                SourceFileName = string.IsNullOrWhiteSpace(entry.SourceFileName) ? entry.PhotoId : entry.SourceFileName,
                ImageUrl = await uploadUrlIssuer.CreateDownloadUrlAsync(entry.PhotoId, cancellationToken),
                Sidecar = ToCollectionSidecar(entry)
            });
        }

        return result;
    }

    private static Dictionary<string, int> BuildOwnershipLookup(
        IReadOnlyList<CardEntry> entries,
        out int totalPhotos,
        out int mappedPhotos)
    {
        totalPhotos = entries.Count;
        mappedPhotos = 0;

        var ownership = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var key = BuildOwnershipKey(entry.SetName, entry.CardNumber);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            mappedPhotos++;
            ownership.TryGetValue(key, out var current);
            ownership[key] = current + 1;
        }

        return ownership;
    }

    private static DateTimeOffset? ParseScannedAtUtc(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;
    }

    private static string BuildOwnershipKey(string? series, string? cardNumber)
    {
        var seriesKey = NormalizeSeriesKey(series);
        var numberKey = NormalizeCardNumber(cardNumber);

        if (string.IsNullOrWhiteSpace(seriesKey) || string.IsNullOrWhiteSpace(numberKey))
        {
            return string.Empty;
        }

        return $"{seriesKey}|{numberKey}";
    }

    private static string NormalizeSeriesNameForSummary(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSeriesKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        normalized = normalized.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);

        normalized = normalized.Replace("NEXTLEVEL", "NL", StringComparison.Ordinal);

        var match = Regex.Match(normalized, "SERIE(?<num>\\d+)NL");
        if (match.Success)
        {
            return $"SERIE{match.Groups["num"].Value}NL";
        }

        match = Regex.Match(normalized, "SERIE(?<num>\\d+)");
        if (match.Success)
        {
            return $"SERIE{match.Groups["num"].Value}";
        }

        return normalized;
    }

    private static string NormalizeCardNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        normalized = Regex.Replace(normalized, "[^A-Z0-9]", string.Empty);

        if (NumberOnlyRegex.IsMatch(normalized) && int.TryParse(normalized, out var numericValue))
        {
            return numericValue.ToString();
        }

        return normalized;
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class SeriesMetadata
    {
        public int? Year { get; init; }
        public string? Logo { get; init; }
        public string? Theme { get; init; }
        public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
    }
}
