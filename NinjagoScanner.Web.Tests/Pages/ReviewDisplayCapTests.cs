using NinjagoScanner.Web.Components.Pages;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Tests.Pages;

public sealed class ReviewDisplayCapTests
{
    private static CardListItem Photo(string photoId) => new()
    {
        PhotoId = photoId,
        SourceFileName = photoId,
        ImageUrl = $"/images/{photoId}",
        AnalysisStatus = AnalysisStatuses.Ok,
        ReviewStatus = ReviewStatuses.Unreviewed
    };

    private static CardReviewGroup Group(int photoCount)
    {
        var photos = Enumerable.Range(0, photoCount).Select(i => Photo($"photo-{i}")).ToArray();
        return new CardReviewGroup
        {
            IsCatchAll = true,
            Photos = photos
        };
    }

    [Fact]
    public void DisplayedPhotos_AtOrUnderCap_ReturnsEveryPhoto()
    {
        var group = Group(Review.MaxDisplayedPhotosPerGroup);

        var displayed = Review.DisplayedPhotos(group);

        Assert.Equal(group.Photos.Count, displayed.Count);
        Assert.Equal(group.Photos, displayed);
    }

    [Fact]
    public void DisplayedPhotos_OverCap_ReturnsOnlyFirst18InOrder()
    {
        var group = Group(Review.MaxDisplayedPhotosPerGroup + 5);

        var displayed = Review.DisplayedPhotos(group);

        Assert.Equal(Review.MaxDisplayedPhotosPerGroup, displayed.Count);
        Assert.Equal(group.Photos.Take(Review.MaxDisplayedPhotosPerGroup), displayed);
    }

    [Fact]
    public void HasHiddenPhotos_AtOrUnderCap_IsFalse()
    {
        var group = Group(Review.MaxDisplayedPhotosPerGroup);

        Assert.False(Review.HasHiddenPhotos(group));
    }

    [Fact]
    public void HasHiddenPhotos_OverCap_IsTrue()
    {
        var group = Group(Review.MaxDisplayedPhotosPerGroup + 1);

        Assert.True(Review.HasHiddenPhotos(group));
    }
}
