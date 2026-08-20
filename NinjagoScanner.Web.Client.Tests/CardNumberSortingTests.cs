using NinjagoScanner.Web.Shared;

namespace NinjagoScanner.Web.Client.Tests;

public sealed class CardNumberSortingTests
{
    [Fact]
    public void BuildSortKey_NumericCardNumbers_SortBeforeAlphanumericOnes()
    {
        var cardNumbers = new[] { "LE1", "10", "2", "XXL1" };

        var ordered = cardNumbers.OrderBy(CardNumberSorting.BuildSortKey, StringComparer.Ordinal).ToArray();

        Assert.Equal(["2", "10", "LE1", "XXL1"], ordered);
    }

    [Fact]
    public void BuildSortKey_AlphanumericCardNumbers_SortByPrefixAlphabetically_ThenByNumber()
    {
        var cardNumbers = new[] { "XXL2", "AB1", "LE3", "XXL1", "LE1" };

        var ordered = cardNumbers.OrderBy(CardNumberSorting.BuildSortKey, StringComparer.Ordinal).ToArray();

        Assert.Equal(["AB1", "LE1", "LE3", "XXL1", "XXL2"], ordered);
    }

    [Theory]
    [InlineData("le5", "LE5")]
    [InlineData(" LE5 ", "LE5")]
    [InlineData("xxl-3", "XXL3")]
    [InlineData("007", "7")]
    public void BuildSortKey_NormalizesUnnormalizedInput_ToSameKeyAsNormalizedEquivalent(string raw, string alreadyNormalized)
    {
        Assert.Equal(CardNumberSorting.BuildSortKey(alreadyNormalized), CardNumberSorting.BuildSortKey(raw));
    }

    [Fact]
    public void BuildSortKey_NonConformingCardNumber_SortsAfterAlphanumericGroup()
    {
        var cardNumbers = new[] { "1A2B", "XXL1", "1" };

        var ordered = cardNumbers.OrderBy(CardNumberSorting.BuildSortKey, StringComparer.Ordinal).ToArray();

        Assert.Equal(["1", "XXL1", "1A2B"], ordered);
    }

    [Fact]
    public void BuildSortKey_NullOrBlank_SortsLast()
    {
        var cardNumbers = new[] { "XXL1", "1", null, "  " };

        var ordered = cardNumbers.OrderBy(CardNumberSorting.BuildSortKey, StringComparer.Ordinal).ToArray();

        string?[] expected = ["1", "XXL1", null, "  "];
        Assert.Equal(expected, ordered);
    }
}
