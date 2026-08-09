using NinjagoScanner.Web.Components.Pages;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Tests.Components;

public sealed class ReviewGroupTitleTests
{
    [Fact]
    public void GroupTitle_ForMatchedGroup_IncludesResolvedCatalogCardName()
    {
        var group = new CardReviewGroup
        {
            IsCatchAll = false,
            SeriesName = "Serie 2",
            CardNumber = "4",
            CardName = "Cole",
            Photos = Array.Empty<CardListItem>()
        };

        var title = Review.GroupTitle(group);

        Assert.Equal("Serie 2 · Nr. 4 · Cole", title);
    }

    [Fact]
    public void GroupTitle_ForCatchAllGroup_OmitsCardName()
    {
        var group = new CardReviewGroup
        {
            IsCatchAll = true,
            Photos = Array.Empty<CardListItem>()
        };

        var title = Review.GroupTitle(group);

        Assert.Equal("Ohne bekannte Serie", title);
    }
}
