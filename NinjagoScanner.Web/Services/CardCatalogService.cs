using System.Globalization;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components.Forms;
using NinjagoScanner.CatalogService.Protos;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Provides upload, sidecar persistence, and catalog-backed lookup operations for scanned card photos.
/// All card photo/sidecar access is delegated to PictureService via gRPC; this class never touches the file system.
/// </summary>
internal sealed class CardCatalogService(string cardPhotosDirectory, long maxUploadBytes, string catalogServiceAddress, string pictureServiceAddress)
{
    private const int UploadChunkSizeBytes = 64 * 1024;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    private static readonly Regex NumberOnlyRegex = new("^\\d+$", RegexOptions.Compiled);

    public string CardPhotosDirectory => cardPhotosDirectory;

    public string CatalogServiceAddress => catalogServiceAddress;

    public string PictureServiceAddress => pictureServiceAddress;

    public long MaxUploadBytes => maxUploadBytes;

    public async Task<string> SaveUploadedPhotoAsync(IBrowserFile file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(file);

        if (file.Size <= 0)
        {
            throw new InvalidOperationException("Die ausgewaehlte Datei ist leer.");
        }

        if (file.Size > maxUploadBytes)
        {
            throw new InvalidOperationException($"Die Datei ist zu gross. Erlaubt sind maximal {maxUploadBytes / (1024 * 1024)} MB.");
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Dateityp wird nicht unterstuetzt. Erlaubt: JPG, PNG, BMP, WEBP.");
        }

        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);
        using var call = client.UploadPhoto(cancellationToken: cancellationToken);

        await call.RequestStream.WriteAsync(new UploadPhotoChunk
        {
            Metadata = new UploadPhotoMetadata
            {
                OriginalFileName = file.Name,
                CardPhotosDirectory = cardPhotosDirectory
            }
        });

        await using (var sourceStream = file.OpenReadStream(maxUploadBytes, cancellationToken))
        {
            var buffer = new byte[UploadChunkSizeBytes];
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var chunkData = bytesRead == buffer.Length
                    ? ByteString.CopyFrom(buffer)
                    : ByteString.CopyFrom(buffer, 0, bytesRead);

                await call.RequestStream.WriteAsync(new UploadPhotoChunk { ChunkData = chunkData });
            }
        }

        await call.RequestStream.CompleteAsync();

        try
        {
            var response = await call;
            return response.ImageFileName;
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.InvalidArgument)
        {
            throw new InvalidOperationException(exception.Status.Detail);
        }
    }

    public async Task<IReadOnlyList<CardListItem>> GetCardsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = await LoadCardEntriesAsync(cancellationToken);

        return entries
            .Select(ToCardListItem)
            .ToArray();
    }

    public async Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            .ThenBy(card => card.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => ToSortKey(card.CardNumber), StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => card.CardName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CollectionOverviewResult
        {
            Cards = cards,
            TotalPhotos = totalPhotos,
            MappedPhotos = mappedPhotos
        };
    }

    public async Task<SeriesSummaryResult> GetSeriesSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            Failed = entries.Count(entry => string.Equals(entry.AnalysisStatus, AnalysisStatuses.Failed, StringComparison.OrdinalIgnoreCase))
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
        cancellationToken.ThrowIfCancellationRequested();

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
        var photos = BuildCardPhotos(photoEntries, series, cardNumber);

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
        string imageFileName,
        CollectionCardSidecarUpdate update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new UpdateSidecarRequest
        {
            ImageFileName = imageFileName,
            CardPhotosDirectory = cardPhotosDirectory,
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

    public async Task UpdateReviewStatusAsync(string imageFileName, string reviewStatus, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new UpdateReviewStatusRequest
        {
            ImageFileName = imageFileName,
            CardPhotosDirectory = cardPhotosDirectory,
            ReviewStatus = reviewStatus
        };

        await client.UpdateReviewStatusAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<CardReviewGroup>> GetReviewGroupsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        var photos = entries.Select(ToCardListItem).ToArray();

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
            .ThenBy(group => group.CardNumber, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CardReviewGroup
            {
                IsCatchAll = false,
                SeriesName = group.SeriesName,
                CardNumber = group.CardNumber,
                CardName = group.CardName,
                Photos = group.Photos.OrderBy(photo => photo.ImageFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            })
            .ToList();

        if (catchAll.Count > 0)
        {
            groups.Add(new CardReviewGroup
            {
                IsCatchAll = true,
                Photos = catchAll.OrderBy(photo => photo.ImageFileName, StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        return groups;
    }

    public async Task<IReadOnlyList<string>> GetKnownSeriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

    public async Task UpdateSetNameAsync(string imageFileName, string? setName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new UpdateSetNameRequest
        {
            ImageFileName = imageFileName,
            CardPhotosDirectory = cardPhotosDirectory,
            SetName = string.IsNullOrWhiteSpace(setName) ? string.Empty : setName.Trim()
        };

        await client.UpdateSetNameAsync(request, cancellationToken: cancellationToken);
    }

    public async Task UpdateCardNumberAsync(string imageFileName, string? cardNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new UpdateCardNumberRequest
        {
            ImageFileName = imageFileName,
            CardPhotosDirectory = cardPhotosDirectory,
            CardNumber = string.IsNullOrWhiteSpace(cardNumber) ? string.Empty : cardNumber.Trim()
        };

        await client.UpdateCardNumberAsync(request, cancellationToken: cancellationToken);
    }

    private async Task<IReadOnlyList<CardEntry>> LoadCardEntriesAsync(CancellationToken cancellationToken)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);
        var response = await client.ListCardsAsync(
            new ListCardsRequest { CardPhotosDirectory = cardPhotosDirectory },
            cancellationToken: cancellationToken);

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

    private static CardListItem ToCardListItem(CardEntry entry)
    {
        return new CardListItem
        {
            ImageFileName = entry.ImageFileName,
            ImageUrl = BuildImageUrl(entry.ImageFileName),
            AnalysisStatus = entry.AnalysisStatus,
            CardName = NormalizeNullable(entry.CardName),
            CardNumber = NormalizeNullable(entry.CardNumber),
            SetName = NormalizeNullable(entry.SetName),
            Rarity = NormalizeNullable(entry.Rarity),
            Confidence = entry.Confidence,
            ReasoningSummary = NormalizeNullable(entry.ReasoningSummary),
            DetectedText = entry.DetectedText.ToArray(),
            ScannedAtUtc = ParseScannedAtUtc(entry.ScannedAtUtc),
            ErrorMessage = NormalizeNullable(entry.ErrorMessage),
            ReviewStatus = NormalizeNullable(entry.ReviewStatus) ?? ReviewStatuses.Unreviewed
        };
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

    private static IReadOnlyList<CollectionCardPhotoItem> BuildCardPhotos(
        IReadOnlyList<CardEntry> entries,
        string series,
        string cardNumber)
    {
        var ownershipKey = BuildOwnershipKey(series, cardNumber);
        if (string.IsNullOrWhiteSpace(ownershipKey))
        {
            return Array.Empty<CollectionCardPhotoItem>();
        }

        return entries
            .Where(entry => string.Equals(BuildOwnershipKey(entry.SetName, entry.CardNumber), ownershipKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.ImageFileName, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new CollectionCardPhotoItem
            {
                ImageFileName = entry.ImageFileName,
                ImageUrl = BuildImageUrl(entry.ImageFileName),
                Sidecar = ToCollectionSidecar(entry)
            })
            .ToArray();
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

    private static string BuildImageUrl(string imageFileName)
    {
        return $"/cardFotos/{Uri.EscapeDataString(imageFileName)}";
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

    private static string ToSortKey(string cardNumber)
    {
        if (int.TryParse(cardNumber, out var numericValue))
        {
            return $"0-{numericValue:D6}";
        }

        if (cardNumber.StartsWith("LE", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(cardNumber.AsSpan(2), out var leValue))
        {
            return $"1-{leValue:D6}";
        }

        if (cardNumber.StartsWith("XXL", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(cardNumber.AsSpan(3), out var xxlValue))
        {
            return $"2-{xxlValue:D6}";
        }

        return $"9-{cardNumber}";
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
