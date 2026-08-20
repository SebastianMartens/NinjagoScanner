using NinjagoScanner.Web.Client.Pages;

namespace NinjagoScanner.Web.Client.Tests.Pages;

public sealed class GalleryIsPuzzleCategoryTests
{
    [Theory]
    [InlineData("Puzzle Cards")]
    [InlineData("Puzzle Cards / Day of the Departed")]
    [InlineData("puzzle cards / day of the departed")]
    public void IsPuzzleCategory_ForPuzzleSubGroup_ReturnsTrue(string category)
    {
        Assert.True(Gallery.IsPuzzleCategory(category));
    }

    [Theory]
    [InlineData("Good Guys")]
    [InlineData("Villains")]
    [InlineData("Unkategorisiert")]
    public void IsPuzzleCategory_ForNonPuzzleCategory_ReturnsFalse(string category)
    {
        Assert.False(Gallery.IsPuzzleCategory(category));
    }
}
