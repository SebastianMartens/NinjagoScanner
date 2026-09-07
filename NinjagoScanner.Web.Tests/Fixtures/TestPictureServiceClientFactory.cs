using Grpc.Net.Client;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>Builds a PictureServiceClient wired to a fixed test collection, for tests that don't specifically exercise collection resolution itself.</summary>
internal static class TestPictureServiceClientFactory
{
    public static PictureServiceClient Create(string pictureServiceAddress, string catalogServiceAddress, long maxUploadBytes)
    {
        var channel = GrpcChannel.ForAddress(pictureServiceAddress);
        return new PictureServiceClient(
            channel,
            catalogServiceAddress,
            maxUploadBytes,
            new TestCurrentCollectionContext(),
            new TestAuthenticationStateProvider());
    }
}
