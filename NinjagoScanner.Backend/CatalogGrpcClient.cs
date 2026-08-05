using Grpc.Net.Client;
using NinjagoScanner.CatalogService.Protos;

namespace NinjagoScanner.Scanner;

internal static class CatalogGrpcClient
{
    public static async Task<IReadOnlyList<SeriesInfo>> LoadSeriesCatalogAsync(string serviceAddress, CancellationToken cancellationToken)
    {
        var channel = GrpcChannel.ForAddress(serviceAddress);
        var client = new CardCatalog.CardCatalogClient(channel);

        var response = await client.ListSeriesAsync(
            new ListSeriesRequest { IncludeKnownCardNames = true },
            cancellationToken: cancellationToken);

        return response.Series
            .Select(series => new SeriesInfo
            {
                Serie = series.SeriesName,
                Jahr = series.Year,
                Besonderheiten = series.SpecialFeatures.ToArray(),
                Sondereditionen = series.SpecialEditions.ToArray(),
                CardNames = series.KnownCardNames.ToArray()
            })
            .ToArray();
    }
}
