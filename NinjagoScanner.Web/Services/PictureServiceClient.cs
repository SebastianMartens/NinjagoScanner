using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Client for the PictureService gRPC service. This class is used to call the CardPictureService endpoints exposed by the PictureService.
/// </summary>
/// <param name="pictureServiceAddress"></param>
internal sealed class PictureServiceClient(string pictureServiceAddress)
{
    public async Task<ScanSummary> ScanAsync(string cardPhotosDirectory, string catalogServiceAddress, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new CardPictureService.CardPictureServiceClient(channel);

        var request = new ScanRequest
        {
            CardPhotosDirectory = cardPhotosDirectory,
            CatalogServiceAddress = catalogServiceAddress
        };

        return await client.ScanAsync(request, cancellationToken: cancellationToken);
    }
}
