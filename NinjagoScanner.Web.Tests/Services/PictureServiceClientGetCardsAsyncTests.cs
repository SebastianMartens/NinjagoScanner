using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

/// <summary>
/// Covers <see cref="PictureServiceClient.GetCardsAsync"/> resolving every photo's download URL
/// straight from the single ListCards call (PictureService includes it on every CardEntry),
/// without any separate download-URL request.
/// </summary>
public sealed class PictureServiceClientGetCardsAsyncTests : IAsyncLifetime
{
    private readonly PictureServiceTestHost pictureHost = new();
    private PictureServiceClient pictureServiceClient = null!;

    static PictureServiceClientGetCardsAsyncTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        pictureHost.WritePhoto("photo-1", Sidecar(setName: "Serie 2", cardNumber: "1"));
        pictureHost.WritePhoto("photo-2", Sidecar(setName: "Serie 2", cardNumber: "2"));
        pictureHost.WritePhoto("photo-3", Sidecar(setName: "Serie 2", cardNumber: "3"));

        pictureServiceClient = new PictureServiceClient(
            pictureServiceAddress: pictureHost.Address,
            catalogServiceAddress: "http://localhost:0",
            maxUploadBytes: 10 * 1024 * 1024);
    }

    public async Task DisposeAsync()
    {
        await pictureHost.DisposeAsync();
    }

    private static string Sidecar(string setName, string cardNumber)
    {
        return $$"""
        {
          "AnalysisStatus": "ok",
          "CardName": "irrelevant",
          "CardNumber": "{{cardNumber}}",
          "SetName": "{{setName}}",
          "Rarity": "Common",
          "Confidence": 0.9,
          "ReviewStatus": "unreviewed"
        }
        """;
    }

    [Fact]
    public async Task GetCardsAsync_ResolvesEveryPhotosImageUrl_FromListCardsDirectly()
    {
        var cards = await pictureServiceClient.GetCardsAsync();

        Assert.Equal(3, cards.Count);
        foreach (var card in cards)
        {
            Assert.False(string.IsNullOrWhiteSpace(card.ImageUrl));
            Assert.Equal($"https://fake-photo-store.test/{card.PhotoId}", card.ImageUrl);
        }
    }
}
