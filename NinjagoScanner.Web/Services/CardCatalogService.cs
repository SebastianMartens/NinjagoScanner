using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

internal sealed class CardCatalogService(string cardPhotosDirectory, long maxUploadBytes)
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

    private readonly string seriesCatalogPath = Path.GetFullPath(Path.Combine(cardPhotosDirectory, "..", "cardInfos", "series.json"));

    public string CardPhotosDirectory => cardPhotosDirectory;

    public string SeriesCatalogPath => seriesCatalogPath;

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

    public Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cardsFromSeries = LoadCardsFromSeriesFiles(cancellationToken);
        var ownershipByKey = LoadOwnedCopiesByCardKey(cancellationToken, out var totalPhotos, out var mappedPhotos);

        var cards = cardsFromSeries
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

        return Task.FromResult(new CollectionOverviewResult
        {
            Cards = cards,
            TotalPhotos = totalPhotos,
            MappedPhotos = mappedPhotos
        });
    }

    public Task<CollectionCardDetails?> GetCollectionCardDetailsAsync(
        string series,
        string category,
        string cardNumber,
        string cardName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cardsFromSeries = LoadCardsFromSeriesFiles(cancellationToken);
        var card = cardsFromSeries.FirstOrDefault(entry =>
            string.Equals(NormalizeSeriesKey(entry.Series), NormalizeSeriesKey(series), StringComparison.Ordinal)
            && string.Equals(entry.CardNumber, NormalizeCardNumber(cardNumber), StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.CardName, cardName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(card.Series))
        {
            return Task.FromResult<CollectionCardDetails?>(null);
        }

        var metadata = LoadSeriesMetadata(series, cancellationToken);
        var photos = LoadCardPhotos(series, cardNumber, cancellationToken);

        return Task.FromResult<CollectionCardDetails?>(new CollectionCardDetails
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
        });
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

    public Task<IReadOnlyList<string>> GetKnownSeriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(seriesCatalogPath))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        try
        {
            var json = File.ReadAllText(seriesCatalogPath);
            var catalog = JsonSerializer.Deserialize<SeriesCatalogRoot>(json, JsonOptions);
            var series = catalog?.Series?
                .Select(entry => entry.Serie?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToArray() ?? Array.Empty<string>();

            return Task.FromResult<IReadOnlyList<string>>(series);
        }
        catch
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    private IReadOnlyList<(string Series, string Category, string CardNumber, string CardName)> LoadCardsFromSeriesFiles(CancellationToken cancellationToken)
    {
        var cardInfosDirectory = Path.GetFullPath(Path.Combine(cardPhotosDirectory, "..", "cardInfos"));
        if (!Directory.Exists(cardInfosDirectory))
        {
            return Array.Empty<(string Series, string Category, string CardNumber, string CardName)>();
        }

        var files = Directory
            .EnumerateFiles(cardInfosDirectory, "series_*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var uniqueCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cards = new List<(string Series, string Category, string CardNumber, string CardName)>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);

            if (!document.RootElement.TryGetProperty(document.RootElement.EnumerateObject().FirstOrDefault().Name, out var seriesRoot))
            {
                continue;
            }

            var seriesName = ToSeriesDisplayName(document.RootElement.EnumerateObject().First().Name);

            foreach (var card in EnumerateCardEntries(seriesRoot, []))
            {
                var normalizedNumber = NormalizeCardNumber(card.CardNumber);
                if (string.IsNullOrWhiteSpace(normalizedNumber) || string.IsNullOrWhiteSpace(card.CardName))
                {
                    continue;
                }

                var normalizedCategory = string.IsNullOrWhiteSpace(card.Category)
                    ? "Unkategorisiert"
                    : card.Category.Trim();

                var uniqueKey = string.Join('|', seriesName, normalizedCategory, normalizedNumber, card.CardName.Trim());
                if (!uniqueCards.Add(uniqueKey))
                {
                    continue;
                }

                cards.Add((seriesName, normalizedCategory, normalizedNumber, card.CardName.Trim()));
            }
        }

        return cards;
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

    private SeriesMetadata LoadSeriesMetadata(string series, CancellationToken cancellationToken)
    {
        var cardInfosDirectory = Path.GetFullPath(Path.Combine(cardPhotosDirectory, "..", "cardInfos"));
        if (!Directory.Exists(cardInfosDirectory))
        {
            return new SeriesMetadata();
        }

        var targetSeriesKey = NormalizeSeriesKey(series);
        var files = Directory
            .EnumerateFiles(cardInfosDirectory, "series_*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            var rootProperty = document.RootElement.EnumerateObject().FirstOrDefault();

            if (string.IsNullOrWhiteSpace(rootProperty.Name))
            {
                continue;
            }

            var currentSeriesName = ToSeriesDisplayName(rootProperty.Name);
            if (!string.Equals(NormalizeSeriesKey(currentSeriesName), targetSeriesKey, StringComparison.Ordinal))
            {
                continue;
            }

            var root = rootProperty.Value;
            var metadata = new SeriesMetadata
            {
                Year = root.TryGetProperty("Jahr", out var yearProperty) && yearProperty.ValueKind == JsonValueKind.Number
                    ? yearProperty.GetInt32()
                    : null,
                Logo = root.TryGetProperty("Logo", out var logoProperty) && logoProperty.ValueKind == JsonValueKind.String
                    ? logoProperty.GetString()
                    : null,
                Theme = root.TryGetProperty("Thema", out var themeProperty) && themeProperty.ValueKind == JsonValueKind.String
                    ? themeProperty.GetString()
                    : null,
                Highlights = root.TryGetProperty("Besonderheiten", out var highlightsProperty) && highlightsProperty.ValueKind == JsonValueKind.Array
                    ? highlightsProperty.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()
                    : Array.Empty<string>()
            };

            return metadata;
        }

        return new SeriesMetadata();
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

    private static IEnumerable<(string CardNumber, string CardName, string Category)> EnumerateCardEntries(
        JsonElement element,
        IReadOnlyList<string> categoryPath)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("Karten-Nr.", out var numberProperty)
                && element.TryGetProperty("Name", out var nameProperty)
                && numberProperty.ValueKind != JsonValueKind.Object
                && nameProperty.ValueKind == JsonValueKind.String)
            {
                var number = numberProperty.ValueKind switch
                {
                    JsonValueKind.Number => numberProperty.GetRawText(),
                    JsonValueKind.String => numberProperty.GetString(),
                    _ => null
                };

                var name = nameProperty.GetString();
                if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(name))
                {
                    yield return (number, name, BuildCategoryLabel(categoryPath));
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("Karten-Nr.") || property.NameEquals("Name"))
                {
                    continue;
                }

                var nextCategoryPath = categoryPath;
                if (ShouldTrackCategory(property.Name))
                {
                    nextCategoryPath = [.. categoryPath, ToCategoryDisplayName(property.Name)];
                }

                foreach (var entry in EnumerateCardEntries(property.Value, nextCategoryPath))
                {
                    yield return entry;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var entry in EnumerateCardEntries(item, categoryPath))
                {
                    yield return entry;
                }
            }
        }
    }

    private static bool ShouldTrackCategory(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var normalized = propertyName.Trim();
        return !normalized.Equals("Jahr", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Logo", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Thema", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Besonderheiten", StringComparison.OrdinalIgnoreCase)
               && !normalized.Equals("Kategorien", StringComparison.OrdinalIgnoreCase)
               && !normalized.StartsWith("Serie", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToCategoryDisplayName(string rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory))
        {
            return "Unkategorisiert";
        }

        var normalized = rawCategory.Trim().Replace('_', ' ');
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return normalized;
    }

    private static string BuildCategoryLabel(IReadOnlyList<string> categoryPath)
    {
        if (categoryPath.Count == 0)
        {
            return "Unkategorisiert";
        }

        return string.Join(" / ", categoryPath);
    }

    private static string ToSeriesDisplayName(string rawSeriesName)
    {
        if (string.IsNullOrWhiteSpace(rawSeriesName))
        {
            return "Unbekannte Serie";
        }

        var normalized = rawSeriesName.Trim().Replace('_', ' ');
        normalized = Regex.Replace(normalized, "\\s+", " ");

        if (normalized.Contains(" NL", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Regex.Replace(normalized, "\\bNL\\b", "Next Level", RegexOptions.IgnoreCase);
        }

        if (Regex.IsMatch(normalized, "^Serie\\s*\\d+NL$", RegexOptions.IgnoreCase))
        {
            normalized = Regex.Replace(normalized, "NL$", " Next Level", RegexOptions.IgnoreCase);
        }

        if (!normalized.StartsWith("Serie", StringComparison.OrdinalIgnoreCase))
        {
            normalized = $"Serie {normalized}";
        }

        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        return normalized;
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

    private sealed class SeriesCatalogRoot
    {
        [JsonPropertyName("Ninjago_Sammelkarten_Serien")]
        public SeriesCatalogEntry[]? Series { get; init; }
    }

    private sealed class SeriesCatalogEntry
    {
        [JsonPropertyName("Serie")]
        public string? Serie { get; init; }
    }

    private sealed class SeriesMetadata
    {
        public int? Year { get; init; }
        public string? Logo { get; init; }
        public string? Theme { get; init; }
        public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
    }
}