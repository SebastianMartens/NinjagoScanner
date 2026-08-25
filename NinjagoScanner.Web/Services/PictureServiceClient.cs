using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>Client for PictureService's bulk-scan RPC.</summary>
internal sealed class PictureServiceClient(string pictureServiceAddress, string catalogServiceAddress)
{
    public async Task<ScanSummaryDto> ScanAsync(CancellationToken cancellationToken = default)
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
