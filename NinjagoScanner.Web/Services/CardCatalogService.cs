using System.Text.Json;
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
        PropertyNameCaseInsensitive = true
    };

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

    private sealed class CardSidecar
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
    }
}