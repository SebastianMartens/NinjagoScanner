using System.Text.Json;
using System.Text.Json.Serialization;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

internal sealed class CardCatalogService(string cardPhotosDirectory)
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

    private readonly string seriesCatalogPath = Path.GetFullPath(Path.Combine(cardPhotosDirectory, "..", "cardInfos", "series.json"));

    public string CardPhotosDirectory => cardPhotosDirectory;

    public string SeriesCatalogPath => seriesCatalogPath;

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
}