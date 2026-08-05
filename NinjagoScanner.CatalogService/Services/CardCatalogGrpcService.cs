using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using NinjagoScanner.CatalogService.Catalog;
using NinjagoScanner.CatalogService.Protos;

namespace NinjagoScanner.CatalogService.Services;

public sealed class CardCatalogGrpcService(CatalogRepository repository) : CardCatalog.CardCatalogBase
{
    public override Task<ListSeriesResponse> ListSeries(ListSeriesRequest request, ServerCallContext context)
    {
        var snapshot = repository.GetSnapshot();
        var response = new ListSeriesResponse();

        foreach (var entry in snapshot.Series)
        {
            response.Series.Add(ToProto(entry, request.IncludeKnownCardNames));
        }

        return Task.FromResult(response);
    }

    public override Task<GetSeriesResponse> GetSeries(GetSeriesRequest request, ServerCallContext context)
    {
        var entry = repository.FindByName(request.SeriesName);
        if (entry is null)
        {
            return Task.FromResult(new GetSeriesResponse
            {
                Found = false
            });
        }

        return Task.FromResult(new GetSeriesResponse
        {
            Found = true,
            Series = ToProto(entry, request.IncludeKnownCardNames)
        });
    }

    public override Task<ListAllCardsResponse> ListAllCards(Empty request, ServerCallContext context)
    {
        var snapshot = repository.GetSnapshot();
        var response = new ListAllCardsResponse();

        foreach (var card in snapshot.Cards)
        {
            response.Cards.Add(new CatalogCardEntry
            {
                SeriesName = card.SeriesName,
                Category = card.Category,
                CardNumber = card.CardNumber,
                CardName = card.CardName
            });
        }

        return Task.FromResult(response);
    }

    public override Task<GetSeriesMetadataResponse> GetSeriesMetadata(GetSeriesMetadataRequest request, ServerCallContext context)
    {
        var metadata = repository.FindSeriesMetadata(request.SeriesName);
        if (metadata is null)
        {
            return Task.FromResult(new GetSeriesMetadataResponse
            {
                Found = false
            });
        }

        var response = new GetSeriesMetadataResponse
        {
            Found = true,
            Metadata = new SeriesMetadata
            {
                SeriesName = metadata.SeriesName,
                Year = metadata.Year ?? 0,
                Logo = metadata.Logo ?? string.Empty,
                Theme = metadata.Theme ?? string.Empty
            }
        };

        response.Metadata.Highlights.AddRange(metadata.Highlights);
        return Task.FromResult(response);
    }

    public override Task<ServiceInfoResponse> GetServiceInfo(Empty request, ServerCallContext context)
    {
        var snapshot = repository.GetSnapshot();

        return Task.FromResult(new ServiceInfoResponse
        {
            DataDirectory = snapshot.DataDirectory,
            SeriesCount = snapshot.Series.Count,
            LoadedAtUtc = snapshot.LoadedAtUtc.UtcDateTime.ToString("O")
        });
    }

    private static SeriesEntry ToProto(SeriesCatalogItem entry, bool includeKnownCardNames)
    {
        var dto = new SeriesEntry
        {
            SeriesName = entry.SeriesName,
            Year = entry.Year
        };

        dto.SpecialFeatures.AddRange(entry.SpecialFeatures);
        dto.SpecialEditions.AddRange(entry.SpecialEditions);

        if (includeKnownCardNames)
        {
            dto.KnownCardNames.AddRange(entry.KnownCardNames);
        }

        return dto;
    }
}
