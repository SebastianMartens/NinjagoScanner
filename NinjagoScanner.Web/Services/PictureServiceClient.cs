using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.Web.Services;

internal sealed class PictureServiceClient(string pictureServiceAddress)
{
    public async Task<ScanSummary> ScanAsync(string cardPhotosDirectory, string catalogServiceAddress, CancellationToken cancellationToken = default)
    {
        using var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        var client = new PictureScanner.PictureScannerClient(channel);

        var request = new ScanRequest
        {
            CardPhotosDirectory = cardPhotosDirectory,
            CatalogServiceAddress = catalogServiceAddress
        };

        return await client.ScanAsync(request, cancellationToken: cancellationToken);
    }
}
