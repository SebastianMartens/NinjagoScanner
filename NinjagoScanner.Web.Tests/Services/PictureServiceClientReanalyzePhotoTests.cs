using Grpc.Core;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class PictureServiceClientReanalyzePhotoTests : IAsyncLifetime
{
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;

    static PictureServiceClientReanalyzePhotoTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-1", """
        {
          "AnalysisStatus": "failed",
          "CardName": "Wrong",
          "CardNumber": "1",
          "SetName": "Serie 9",
          "ReviewStatus": "incorrect"
        }
        """);

        pictureServiceClient = TestPictureServiceClientFactory.Create(
            pictureServiceAddress: pictureHost.Address,
            catalogServiceAddress: "http://localhost:0",
            maxUploadBytes: 10 * 1024 * 1024);
    }

    public async Task DisposeAsync()
    {
        await pictureHost.DisposeAsync();
    }

    [Fact]
    public async Task ReanalyzePhotoAsync_ReturnsUpdatedCard_AndPreservesReviewStatus()
    {
        pictureHost.SetReanalysisResult("Cole", "4", "Serie 2");

        var card = await pictureServiceClient.ReanalyzePhotoAsync("photo-1");

        Assert.Equal("photo-1", card.PhotoId);
        Assert.Equal("ok", card.AnalysisStatus);
        Assert.Equal("Cole", card.CardName);
        Assert.Equal("4", card.CardNumber);
        Assert.Equal("Serie 2", card.SetName);
        Assert.Equal("incorrect", card.ReviewStatus);
    }

    [Fact]
    public async Task ReanalyzePhotoAsync_WritesResultWithinTheCallersCollection()
    {
        pictureHost.SetReanalysisResult("Cole", "4", "Serie 2");

        await pictureServiceClient.ReanalyzePhotoAsync("photo-1");

        var cards = await pictureServiceClient.GetCardsAsync();
        var card = Assert.Single(cards);
        Assert.Equal("Cole", card.CardName);
        Assert.Equal("ok", card.AnalysisStatus);
    }

    [Fact]
    public async Task ReanalyzePhotoAsync_ThrowsNotFound_WhenPhotoIsOnlyInAnotherCollection()
    {
        pictureHost.WritePhoto("photo-elsewhere", collectionId: "another-collection");

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => pictureServiceClient.ReanalyzePhotoAsync("photo-elsewhere"));

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task ReanalyzePhotoAsync_ThrowsRpcException_AndLeavesCardUntouched_WhenServiceIsUnavailable()
    {
        pictureHost.ReanalyzeFailureStatusCode = StatusCode.Unavailable;

        var exception = await Assert.ThrowsAsync<RpcException>(
            () => pictureServiceClient.ReanalyzePhotoAsync("photo-1"));

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        var card = Assert.Single(await pictureServiceClient.GetCardsAsync());
        Assert.Equal("failed", card.AnalysisStatus);
        Assert.Equal("Wrong", card.CardName);
    }
}
