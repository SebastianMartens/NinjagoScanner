using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class PictureServiceClientUpdateRotationTests : IAsyncLifetime
{
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;

    static PictureServiceClientUpdateRotationTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-1", """
        {
          "AnalysisStatus": "ok",
          "CardName": "Kai",
          "CardNumber": "1",
          "SetName": "Serie 1",
          "ReviewStatus": "verified"
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
    public async Task UpdateRotationAsync_SetsRotated180_AndLeavesOtherFieldsUntouched()
    {
        await pictureServiceClient.UpdateRotationAsync("photo-1", rotated180: true);

        var card = Assert.Single(await pictureServiceClient.GetCardsAsync());
        Assert.True(card.Rotated180);
        Assert.Equal("ok", card.AnalysisStatus);
        Assert.Equal("Kai", card.CardName);
        Assert.Equal("1", card.CardNumber);
        Assert.Equal("Serie 1", card.SetName);
        Assert.Equal("verified", card.ReviewStatus);
    }

    [Fact]
    public async Task UpdateRotationAsync_TogglesBack()
    {
        await pictureServiceClient.UpdateRotationAsync("photo-1", rotated180: true);
        await pictureServiceClient.UpdateRotationAsync("photo-1", rotated180: false);

        var card = Assert.Single(await pictureServiceClient.GetCardsAsync());
        Assert.False(card.Rotated180);
    }

    [Fact]
    public async Task UpdateRotationAsync_DefaultsToFalse_ForAPhotoNeverRotated()
    {
        var card = Assert.Single(await pictureServiceClient.GetCardsAsync());

        Assert.False(card.Rotated180);
    }
}
