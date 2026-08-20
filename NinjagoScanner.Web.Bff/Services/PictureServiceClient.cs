using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Shared.Models;

namespace NinjagoScanner.Web.Bff.Services;

/// <summary>Client for PictureService's bulk-scan RPC. Ported from the former NinjagoScanner.Web/Services/PictureServiceClient.cs.</summary>
internal sealed class PictureServiceClient(string pictureServiceAddress)
{
    public async Task<ScanSummaryDto> ScanAsync(string catalogServiceAddress, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var response = await client.ScanAsync(
            new ScanRequest { CatalogServiceAddress = catalogServiceAddress },
            cancellationToken: cancellationToken);

        return new ScanSummaryDto
        {
            TotalImages = response.TotalImages,
            Processed = response.Processed,
            Skipped = response.Skipped,
            Uncertain = response.Uncertain,
            Failed = response.Failed,
            HasConfigurationError = response.HasConfigurationError,
            Message = string.IsNullOrWhiteSpace(response.Message) ? null : response.Message
        };
    }
}
