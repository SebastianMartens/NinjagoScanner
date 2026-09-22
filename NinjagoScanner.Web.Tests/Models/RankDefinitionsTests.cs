using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Tests.Models;

public sealed class RankDefinitionsTests
{
    [Theory]
    [InlineData(0, 1, "Novize")]
    [InlineData(149, 1, "Novize")]
    [InlineData(150, 2, "Schüler")]
    [InlineData(399, 2, "Schüler")]
    [InlineData(400, 3, "Spinjitzu-Schüler")]
    [InlineData(799, 3, "Spinjitzu-Schüler")]
    [InlineData(800, 4, "Funke")]
    [InlineData(13999, 9, "Sensei")]
    [InlineData(14000, 10, "Goldener Ninja")]
    [InlineData(999999, 10, "Goldener Ninja")]
    public void ForXp_ExactThresholdCrossesToTheNewRank(int xp, int expectedLevel, string expectedName)
    {
        var rank = RankDefinitions.ForXp(xp);

        Assert.Equal(expectedLevel, rank.Level);
        Assert.Equal(expectedName, rank.Name);
    }

    [Fact]
    public void Next_OnHighestRank_ReturnsNull()
    {
        var highest = RankDefinitions.All[^1];

        Assert.Null(RankDefinitions.Next(highest));
    }

    [Fact]
    public void Next_ReturnsTheFollowingRankByLevel()
    {
        var novize = RankDefinitions.All[0];

        var next = RankDefinitions.Next(novize);

        Assert.NotNull(next);
        Assert.Equal("Schüler", next!.Name);
    }
}
