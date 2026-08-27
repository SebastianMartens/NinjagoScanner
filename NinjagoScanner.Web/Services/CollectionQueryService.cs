using System.Text.RegularExpressions;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Merges catalog data (from CatalogServiceClient) with scanned photo data (from
/// PictureServiceClient) into the shapes the app's Razor pages render: matching a photo's
/// SetName/CardNumber to a catalog card, grouping, sorting, and counting ownership. Never opens
/// its own gRPC channel - all catalog/picture access goes through the two injected clients.
/// </summary>
internal sealed class CollectionQueryService(
    CatalogServiceClient catalogServiceClient,
    PictureServiceClient pictureServiceClient)
{
    private static readonly Regex NumberOnlyRegex = new("^\\d+$", RegexOptions.Compiled);

    public async Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var photoEntries = await pictureServiceClient.ListCardEntriesAsync(cancellationToken);
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
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var photoEntries = await pictureServiceClient.ListCardEntriesAsync(cancellationToken);
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
                ImageUrl = matchedPhoto?.DownloadUrl,
                PhotoCount = photoCount,
                Rarity = matchedPhoto?.Rarity,
                ReviewStatus = matchedPhoto is null ? null : NormalizeNullable(matchedPhoto.ReviewStatus)
            });
        }

        return result;
    }

    public async Task<SeriesSummaryResult> GetSeriesSummaryAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var photoEntries = await pictureServiceClient.ListCardEntriesAsync(cancellationToken);

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

    public async Task<CollectionCardDetails?> GetCollectionCardDetailsAsync(
        string series,
        string cardNumber,
        CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
        var card = cardsFromCatalog.FirstOrDefault(entry =>
            string.Equals(NormalizeSeriesKey(entry.Series), NormalizeSeriesKey(series), StringComparison.Ordinal)
            && string.Equals(entry.CardNumber, NormalizeCardNumber(cardNumber), StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(card.Series))
        {
            return null;
        }

        var metadata = await catalogServiceClient.GetSeriesMetadataAsync(series, cancellationToken);
        var photoEntries = await pictureServiceClient.ListCardEntriesAsync(cancellationToken);
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

    public async Task<IReadOnlyList<CardReviewGroup>> GetReviewGroupsAsync(CancellationToken cancellationToken = default)
    {
        var cardsFromCatalog = await catalogServiceClient.ListCatalogCardsAsync(cancellationToken);
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

        var photos = await pictureServiceClient.GetCardsAsync(cancellationToken);

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

    private static CollectionCardSidecarData ToCollectionSidecar(CardEntry entry, CardDetailsItem details)
    {
        return new CollectionCardSidecarData
        {
            AnalysisStatus = entry.AnalysisStatus,
            CardName = NormalizeNullable(entry.CardName),
            CardNumber = NormalizeNullable(entry.CardNumber),
            SetName = NormalizeNullable(entry.SetName),
            Rarity = NormalizeNullable(entry.Rarity),
            Language = NormalizeNullable(entry.Language) ?? Languages.Default,
            Confidence = details.Confidence,
            ReasoningSummary = details.ReasoningSummary,
            DetectedText = details.DetectedText,
            ScannedAtUtc = details.ScannedAtUtc,
            ErrorMessage = details.ErrorMessage,
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
            var details = await pictureServiceClient.GetCardDetailsAsync(entry.PhotoId, cancellationToken);
            result.Add(new CollectionCardPhotoItem
            {
                PhotoId = entry.PhotoId,
                SourceFileName = string.IsNullOrWhiteSpace(entry.SourceFileName) ? entry.PhotoId : entry.SourceFileName,
                ImageUrl = entry.DownloadUrl,
                Sidecar = ToCollectionSidecar(entry, details)
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
}
