using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components.Forms;
using NinjagoScanner.CatalogService.Protos;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

internal sealed class CardCatalogService(string cardPhotosDirectory, long maxUploadBytes, string catalogServiceAddress)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex NumberOnlyRegex = new("^\\d+$", RegexOptions.Compiled);
    private static readonly Regex UnsafeFileNameRegex = new("[^A-Za-z0-9_-]+", RegexOptions.Compiled);

    public string CardPhotosDirectory => cardPhotosDirectory;

    public string CatalogServiceAddress => catalogServiceAddress;

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

        Directory.CreateDirectory(cardPhotosDirectory);

        var fileNameStem = BuildUploadFileNameStem(file.Name);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidateName = attempt == 0
                ? $"{fileNameStem}{extension}"
                : $"{fileNameStem}-{attempt}{extension}";
            var destinationPath = Path.Combine(cardPhotosDirectory, candidateName);

            try
            {
                await using var sourceStream = file.OpenReadStream(maxUploadBytes, cancellationToken);
                await using var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true);
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);

                return candidateName;
            }
            catch (IOException) when (File.Exists(destinationPath))
            {
                continue;
            }
        }

        throw new IOException("Es konnte kein eindeutiger Dateiname fuer den Upload erstellt werden.");
    }

    public async Task<IReadOnlyList<CardListItem>> GetCardsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(cardPhotosDirectory))
        {
            return Array.Empty<CardListItem>();
        }

        var imageFiles = Directory
            .EnumerateFiles(cardPhotosDirectory)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cards = new List<CardListItem>(imageFiles.Count);

        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imageFileName = Path.GetFileName(imagePath);
            var sidecarPath = imagePath + ".json";
            var imageUrl = $"/cardFotos/{Uri.EscapeDataString(imageFileName)}";

            if (!File.Exists(sidecarPath))
            {
                cards.Add(new CardListItem
                {
                    ImageFileName = imageFileName,
                    ImageUrl = imageUrl,
                    Status = "pending"
                });

                continue;
            }

            try
            {
                await using var stream = File.OpenRead(sidecarPath);
                var sidecar = await JsonSerializer.DeserializeAsync<CardSidecar>(stream, JsonOptions, cancellationToken);

                cards.Add(new CardListItem
                {
                    ImageFileName = imageFileName,
                    ImageUrl = imageUrl,
                    Status = sidecar?.Status ?? "unknown",
                    CardName = sidecar?.CardName,
                    CardNumber = sidecar?.CardNumber,
                    SetName = sidecar?.SetName,
                    Rarity = sidecar?.Rarity,
                    Confidence = sidecar?.Confidence ?? 0,
                    ReasoningSummary = sidecar?.ReasoningSummary,
                    DetectedText = sidecar?.DetectedText ?? Array.Empty<string>(),
                    ScannedAtUtc = sidecar?.ScannedAtUtc,
                    ErrorMessage = sidecar?.ErrorMessage
                });
            }
            catch (Exception exception)
            {
                cards.Add(new CardListItem
                {
                    ImageFileName = imageFileName,
                    ImageUrl = imageUrl,
                    Status = "failed",
                    ErrorMessage = $"Sidecar konnte nicht gelesen werden: {exception.Message}"
                });
            }
        }

        return cards;
    }

    public async Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var ownershipByKey = LoadOwnedCopiesByCardKey(cancellationToken, out var totalPhotos, out var mappedPhotos);

        var cards = cardsFromCatalog
            .Select(card =>
            {
                ownershipByKey.TryGetValue(BuildOwnershipKey(card.Series, card.CardNumber), out var ownedCopies);
                return new CollectionCardItem
                {
                    Series = card.Series,
                    Category = card.Category,
                    CardNumber = card.CardNumber,
                    CardName = card.CardName,
                    OwnedCopies = ownedCopies
                };
            })
            .OrderBy(card => card.Series, StringComparer.OrdinalIgnoreCase)
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

    public async Task<CollectionCardDetails?> GetCollectionCardDetailsAsync(
        string series,
        string category,
        string cardNumber,
        string cardName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cardsFromCatalog = await LoadCardsFromCatalogServiceAsync(cancellationToken);
        var card = cardsFromCatalog.FirstOrDefault(entry =>
            string.Equals(NormalizeSeriesKey(entry.Series), NormalizeSeriesKey(series), StringComparison.Ordinal)
            && string.Equals(entry.CardNumber, NormalizeCardNumber(cardNumber), StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.CardName, cardName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(card.Series))
        {
            return null;
        }

        var metadata = await LoadSeriesMetadataAsync(series, cancellationToken);
        var photos = LoadCardPhotos(series, cardNumber, cancellationToken);

        return new CollectionCardDetails
        {
            Series = card.Series,
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

        var imagePath = Path.Combine(cardPhotosDirectory, imageFileName);
        var sidecarPath = imagePath + ".json";

        CardSidecar existing;
        if (File.Exists(sidecarPath))
        {
            await using var readStream = File.OpenRead(sidecarPath);
            existing = await JsonSerializer.DeserializeAsync<CardSidecar>(readStream, JsonOptions, cancellationToken)
                ?? new CardSidecar();
        }
        else
        {
            existing = new CardSidecar
            {
                SourceFileName = imageFileName,
                SourceFilePath = imagePath,
                SidecarFilePath = sidecarPath
            };
        }

        var updated = existing with
        {
            Status = NormalizeNullable(update.Status),
            CardName = NormalizeNullable(update.CardName),
            CardNumber = NormalizeNullable(update.CardNumber),
            SetName = NormalizeNullable(update.SetName),
            Rarity = NormalizeNullable(update.Rarity),
            Confidence = update.Confidence,
            ReasoningSummary = NormalizeNullable(update.ReasoningSummary),
            DetectedText = update.DetectedText.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()).ToArray(),
            ErrorMessage = NormalizeNullable(update.ErrorMessage),
            SourceFileName = existing.SourceFileName ?? imageFileName,
            SourceFilePath = existing.SourceFilePath ?? imagePath,
            SidecarFilePath = existing.SidecarFilePath ?? sidecarPath
        };

        await using var writeStream = File.Create(sidecarPath);
        await JsonSerializer.SerializeAsync(writeStream, updated, JsonOptions, cancellationToken);
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
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<(string Series, string Category, string CardNumber, string CardName)>> LoadCardsFromCatalogServiceAsync(CancellationToken cancellationToken)
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
                    CardName: card.CardName?.Trim() ?? string.Empty
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

    private IReadOnlyList<CollectionCardPhotoItem> LoadCardPhotos(
        string series,
        string cardNumber,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(cardPhotosDirectory))
        {
            return Array.Empty<CollectionCardPhotoItem>();
        }

        var ownershipKey = BuildOwnershipKey(series, cardNumber);
        if (string.IsNullOrWhiteSpace(ownershipKey))
        {
            return Array.Empty<CollectionCardPhotoItem>();
        }

        var photos = new List<CollectionCardPhotoItem>();
        var imageFiles = Directory
            .EnumerateFiles(cardPhotosDirectory)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var imageFileName = Path.GetFileName(imagePath);
            var sidecarPath = imagePath + ".json";
            if (!File.Exists(sidecarPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(sidecarPath);
                var sidecar = JsonSerializer.Deserialize<CardSidecar>(stream, JsonOptions);
                if (sidecar is null)
                {
                    continue;
                }

                var sidecarKey = BuildOwnershipKey(sidecar.SetName, sidecar.CardNumber);
                if (!string.Equals(sidecarKey, ownershipKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                photos.Add(new CollectionCardPhotoItem
                {
                    ImageFileName = imageFileName,
                    ImageUrl = $"/cardFotos/{Uri.EscapeDataString(imageFileName)}",
                    Sidecar = ToCollectionSidecar(sidecar)
                });
            }
            catch
            {
                // Ignore invalid sidecars in detail view aggregation.
            }
        }

        return photos;
    }

    private Dictionary<string, int> LoadOwnedCopiesByCardKey(
        CancellationToken cancellationToken,
        out int totalPhotos,
        out int mappedPhotos)
    {
        totalPhotos = 0;
        mappedPhotos = 0;

        var ownership = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(cardPhotosDirectory))
        {
            return ownership;
        }

        var imageFiles = Directory
            .EnumerateFiles(cardPhotosDirectory)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .ToArray();

        totalPhotos = imageFiles.Length;

        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sidecarPath = imagePath + ".json";
            if (!File.Exists(sidecarPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(sidecarPath);
                var sidecar = JsonSerializer.Deserialize<CardSidecar>(stream, JsonOptions);

                if (string.IsNullOrWhiteSpace(sidecar?.SetName) || string.IsNullOrWhiteSpace(sidecar.CardNumber))
                {
                    continue;
                }

                var key = BuildOwnershipKey(sidecar.SetName, sidecar.CardNumber);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                mappedPhotos++;
                ownership.TryGetValue(key, out var current);
                ownership[key] = current + 1;
            }
            catch
            {
                // Ignore broken sidecars for overview aggregation.
            }
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

    private static CollectionCardSidecarData ToCollectionSidecar(CardSidecar sidecar)
    {
        return new CollectionCardSidecarData
        {
            Status = sidecar.Status,
            CardName = sidecar.CardName,
            CardNumber = sidecar.CardNumber,
            SetName = sidecar.SetName,
            Rarity = sidecar.Rarity,
            Confidence = sidecar.Confidence,
            ReasoningSummary = sidecar.ReasoningSummary,
            DetectedText = sidecar.DetectedText ?? Array.Empty<string>(),
            ScannedAtUtc = sidecar.ScannedAtUtc,
            ErrorMessage = sidecar.ErrorMessage
        };
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

    public async Task UpdateSetNameAsync(string imageFileName, string? setName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var imagePath = Path.Combine(cardPhotosDirectory, imageFileName);
        var sidecarPath = imagePath + ".json";

        CardSidecar sidecar;

        if (File.Exists(sidecarPath))
        {
            await using var readStream = File.OpenRead(sidecarPath);
            sidecar = await JsonSerializer.DeserializeAsync<CardSidecar>(readStream, JsonOptions, cancellationToken)
                ?? new CardSidecar();
        }
        else
        {
            sidecar = new CardSidecar
            {
                Status = "pending"
            };
        }

        sidecar = sidecar with
        {
            SetName = string.IsNullOrWhiteSpace(setName) ? null : setName.Trim()
        };

        await using var writeStream = File.Create(sidecarPath);
        await JsonSerializer.SerializeAsync(writeStream, sidecar, JsonOptions, cancellationToken);
    }

    private sealed record CardSidecar
    {
        public string? Status { get; init; }
        public string? CardName { get; init; }
        public string? CardNumber { get; init; }
        public string? SetName { get; init; }
        public string? Rarity { get; init; }
        public double Confidence { get; init; }
        public string? ReasoningSummary { get; init; }
        public string[]? DetectedText { get; init; }
        public DateTimeOffset? ScannedAtUtc { get; init; }
        public string? ErrorMessage { get; init; }
        public string? SourceFileName { get; init; }
        public string? SourceFilePath { get; init; }
        public string? SidecarFilePath { get; init; }
        public string? AiModel { get; init; }
        public string? RawModelResponse { get; init; }
    }

    private sealed class SeriesMetadata
    {
        public int? Year { get; init; }
        public string? Logo { get; init; }
        public string? Theme { get; init; }
        public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
    }
}
