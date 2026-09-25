using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class GamificationServiceTests : IAsyncLifetime
{
    private readonly CatalogServiceTestHost catalogHost = new();
    private readonly PictureServiceTestHost pictureHost = new();
    private TestGamificationServiceHandle handle = null!;

    static GamificationServiceTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        // Two series so "every-series"/"series-complete" don't trigger incidentally from a single
        // series being touched - each test seeds only the photos it needs to isolate its scenario.
        catalogHost.WriteCatalogFile("series_test.json", """
        {
          "Serie_2": {
            "SortOrder": 2,
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": 4, "Name": {"de": "Cole"}},
                {"Karten-Nr.": 5, "Name": {"de": "Zane"}},
                {"Karten-Nr.": 6, "Name": {"de": "Jay"}}
              ] }
            }
          },
          "Serie_10": {
            "SortOrder": 10,
            "Kategorien": {
              "Good_Guys": { "Class": "character", "Karten": [
                {"Karten-Nr.": 1, "Name": {"de": "Wu"}}
              ] }
            }
          }
        }
        """);

        await catalogHost.StartAsync();
        await pictureHost.StartAsync();

        handle = await TestGamificationServiceFactory.CreateAsync(catalogHost.Address, pictureHost.Address);
    }

    public async Task DisposeAsync()
    {
        await handle.DisposeAsync();
        await catalogHost.DisposeAsync();
        await pictureHost.DisposeAsync();
    }

    private static string Sidecar(string setName, string cardNumber, string reviewStatus = "unreviewed", string rarity = "")
    {
        return $$"""
        {
          "AnalysisStatus": "ok",
          "CardName": "irrelevant",
          "CardNumber": "{{cardNumber}}",
          "SetName": "{{setName}}",
          "Rarity": "{{rarity}}",
          "ReviewStatus": "{{reviewStatus}}"
        }
        """;
    }

    [Fact]
    public async Task GetProgressAsync_FirstScan_UnlocksAfterOnePhoto()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));

        var progress = await handle.Service.GetProgressAsync();

        var firstScan = progress.Single(item => item.Achievement.Id == "first-scan");
        Assert.True(firstScan.IsUnlocked);
        Assert.Equal(1, firstScan.Current);
        Assert.Equal(1, firstScan.Goal);
    }

    [Fact]
    public async Task GetProgressAsync_TenCards_TracksDistinctOwnedCardCount()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "5"));
        // A duplicate copy of an already-owned card must not inflate the distinct-card count.
        pictureHost.WritePhoto("photo-3", Sidecar("Serie 2", "5"));

        var progress = await handle.Service.GetProgressAsync();

        var tenCards = progress.Single(item => item.Achievement.Id == "ten-cards");
        Assert.False(tenCards.IsUnlocked);
        Assert.Equal(2, tenCards.Current);
        Assert.Equal(10, tenCards.Goal);
    }

    [Fact]
    public async Task GetProgressAsync_DupesTwentyFive_CountsExtraCopiesOnly()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-3", Sidecar("Serie 2", "4"));

        var progress = await handle.Service.GetProgressAsync();

        var dupes = progress.Single(item => item.Achievement.Id == "dupes-25");
        Assert.Equal(2, dupes.Current);
    }

    [Fact]
    public async Task GetProgressAsync_EverySeries_GoalIsCatalogSeriesCount()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));

        var progress = await handle.Service.GetProgressAsync();

        var everySeries = progress.Single(item => item.Achievement.Id == "every-series");
        Assert.Equal(2, everySeries.Goal);
        Assert.Equal(1, everySeries.Current);
        Assert.False(everySeries.IsUnlocked);
    }

    [Fact]
    public async Task GetProgressAsync_FirstLegendary_MatchesRarityCaseInsensitively()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4", rarity: "LEGENDARY"));

        var progress = await handle.Service.GetProgressAsync();

        var legendary = progress.Single(item => item.Achievement.Id == "first-legendary");
        Assert.True(legendary.IsUnlocked);
    }

    [Fact]
    public async Task GetXpAsync_SumsAchievementCardAndReviewXp_WithNothingForDuplicates()
    {
        // Cole owned twice (1 duplicate copy), Zane owned once with a verified review.
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-3", Sidecar("Serie 2", "5", reviewStatus: "verified"));

        var xp = await handle.Service.GetXpAsync();

        // first-scan (20) + 2 distinct cards * 50 + 1 verified review * 10 - the duplicate adds nothing
        Assert.Equal(20 + 2 * 50 + 1 * 10, xp);
    }

    [Fact]
    public async Task EvaluateAsync_AddingDuplicateCopy_LeavesXpUnchanged()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        var xpBefore = (await handle.Service.EvaluateAsync(0)).XpAfter; // persists first-scan up front

        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "4"));
        var evaluation = await handle.Service.EvaluateAsync(xpBefore);

        Assert.Empty(evaluation.NewlyUnlockedAchievements);
        Assert.Equal(xpBefore, evaluation.XpAfter);
    }

    [Fact]
    public async Task EvaluateAsync_UnlockingSameAchievementTwice_DoesNotDuplicateOrRefire()
    {
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));

        var firstXp = await handle.Service.GetXpAsync();
        var firstEvaluation = await handle.Service.EvaluateAsync(0);
        Assert.Contains(firstEvaluation.NewlyUnlockedAchievements, a => a.Id == "first-scan");

        var secondEvaluation = await handle.Service.EvaluateAsync(firstXp);
        Assert.DoesNotContain(secondEvaluation.NewlyUnlockedAchievements, a => a.Id == "first-scan");
        Assert.Equal(firstEvaluation.XpAfter, secondEvaluation.XpAfter);
    }

    [Fact]
    public async Task ReviewCorrectionThatRemovesACard_XpAndAchievementUnlockRecomputeDownward()
    {
        // Serie 2 has exactly 3 cards (#4, #5, #6) - owning all three completes it, unlocking
        // first-scan and series-complete alongside the 3 owned cards. Serie 10 stays untouched,
        // so every-series never enters into this scenario.
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "5"));
        pictureHost.WritePhoto("photo-3", Sidecar("Serie 2", "6"));

        var xpBefore = await handle.Service.GetXpAsync();
        await handle.Service.EvaluateAsync(0); // persists the unlocks reached above

        var progressBefore = await handle.Service.GetProgressAsync();
        Assert.True(progressBefore.Single(a => a.Achievement.Id == "series-complete").IsUnlocked);
        Assert.Equal(20 + 500 + 3 * 50, xpBefore);

        // A review correction determines photo-1 doesn't actually match any catalog card.
        pictureHost.WritePhoto("photo-1", Sidecar(setName: "", cardNumber: ""));

        var xpAfter = await handle.Service.GetXpAsync();
        var progressAfter = await handle.Service.GetProgressAsync();

        Assert.True(xpAfter < xpBefore);
        Assert.Equal(20 + 2 * 50, xpAfter);
        // series-complete is no longer true live, even though its AchievementUnlock row (the
        // historical "first crossed" timestamp) is never deleted - see GamificationService.
        Assert.False(progressAfter.Single(a => a.Achievement.Id == "series-complete").IsUnlocked);
    }

    [Fact]
    public async Task EvaluateAsync_RankCrossedFromXpGain_IsReportedAsRankedUp()
    {
        // Schüler starts at 150 XP; completing Serie 2 (500 XP for series-complete alone) crosses
        // it from a standing start of 0.
        pictureHost.WritePhoto("photo-1", Sidecar("Serie 2", "4"));
        pictureHost.WritePhoto("photo-2", Sidecar("Serie 2", "5"));
        pictureHost.WritePhoto("photo-3", Sidecar("Serie 2", "6"));

        var evaluation = await handle.Service.EvaluateAsync(0);

        Assert.True(evaluation.RankedUp);
        Assert.Equal(1, evaluation.RankBefore.Level);
        Assert.True(evaluation.RankAfter.Level > 1);
    }
}
