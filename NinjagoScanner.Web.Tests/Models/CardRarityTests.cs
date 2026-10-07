using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Tests.Models;

public sealed class CardRarityTests
{
    [Theory]
    [InlineData("limited", "limited")]
    [InlineData(" LIMITED ", "limited")]
    [InlineData("legendary", "legendary")]
    [InlineData("common", "common")]
    [InlineData("rare", "common")]
    [InlineData("", "common")]
    [InlineData(null, "common")]
    public void Normalize_KeepsKnownRaritiesAndFallsBackToCommon(string? input, string expected)
    {
        Assert.Equal(expected, CardRarity.Normalize(input));
    }

    [Theory]
    [InlineData("common", "Normal")]
    [InlineData("limited", "Limited")]
    [InlineData("legendary", "Legendär")]
    public void Label_IsGerman(string rarity, string expected)
    {
        Assert.Equal(expected, CardRarity.Label(rarity));
    }

    [Theory]
    [InlineData("limited", "Limited")]
    [InlineData("legendary", "Legendär")]
    public void TagsForRarity_ShowsATagForNonCommonCards(string rarity, string expectedTag)
    {
        Assert.Equal([expectedTag], CardTagHelper.TagsForRarity(rarity));
    }

    [Theory]
    [InlineData("common")]
    [InlineData(null)]
    public void TagsForRarity_ShowsNoTagForCommonCards(string? rarity)
    {
        Assert.Empty(CardTagHelper.TagsForRarity(rarity));
        Assert.Null(CardRarity.BadgeLabel(rarity));
    }
}
