using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using NinjagoScanner.CatalogService.Protos;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// gRPC client for CatalogService only - reads series/card catalog data. Never touches photos,
/// sidecars, or PictureService; see CollectionQueryService for anything that combines catalog
/// data with scanned photo data.
/// </summary>
internal sealed class CatalogServiceClient
{
    private static readonly Regex NumberOnlyRegex = new("^\\d+$", RegexOptions.Compiled);

    private readonly GrpcChannel channel;

    public CatalogServiceClient(string catalogServiceAddress)
    {
        channel = GrpcChannel.ForAddress(catalogServiceAddress, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            }
        });
    }

    public async Task<IReadOnlyList<string>> GetKnownSeriesAsync(CancellationToken cancellationToken = default)
    {
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

    public async Task<IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)>> ListCatalogCardsAsync(CancellationToken cancellationToken = default)
    {
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

    public async Task<SeriesMetadata> GetSeriesMetadataAsync(string series, CancellationToken cancellationToken = default)
    {
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

    public sealed class SeriesMetadata
    {
        public int? Year { get; init; }
        public string? Logo { get; init; }
        public string? Theme { get; init; }
        public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
    }
}
