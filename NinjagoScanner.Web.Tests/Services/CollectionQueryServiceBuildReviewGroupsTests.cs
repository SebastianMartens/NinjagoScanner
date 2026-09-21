using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class CollectionQueryServiceBuildReviewGroupsTests
{
    private static readonly IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)> Catalog =
    [
        ("Serie 2", "Good Guys", "10", "Ten", 2),
        ("Serie 2", "Good Guys", "2", "Two", 2),
        ("Serie 2", "Limited Edition", "LE1", "LE One", 2),
        ("Serie 10", "Good Guys", "1", "Kai", 10)
    ];

    private static CardListItem Photo(string photoId, string? setName, string? cardNumber) => new()
    {
        PhotoId = photoId,
        SourceFileName = photoId,
        ImageUrl = $"https://example.test/{photoId}?sig=abc",
        AnalysisStatus = AnalysisStatuses.Ok,
        ReviewStatus = ReviewStatuses.Unreviewed,
        SetName = setName,
        CardNumber = cardNumber
    };

    [Fact]
    public void BuildReviewGroups_OrdersBySeriesThenCardNumber_AndPutsCatchAllFirst()
    {
        var photos = new[]
        {
            Photo("p-unknown", "Unknown Series", "1"),
            Photo("p-kai", "Serie 10", "1"),
            Photo("p-le1", "Serie 2", "LE1"),
            Photo("p-10", "Serie 2", "10"),
            Photo("p-2", "Serie 2", "2")
        };

        var groups = CollectionQueryService.BuildReviewGroups(Catalog, photos);

        string?[] expected = [null, "2", "10", "LE1", "1"];
        Assert.Equal(expected, groups.Select(group => group.CardNumber).ToArray());
        Assert.True(groups[0].IsCatchAll);
        Assert.Equal("p-unknown", Assert.Single(groups[0].Photos).PhotoId);
    }

    [Fact]
    public void BuildReviewGroups_WithoutUnresolvedPhotos_HasNoCatchAllGroup()
    {
        var photos = new[]
        {
            Photo("p-kai", "Serie 10", "1"),
            Photo("p-10", "Serie 2", "10"),
            Photo("p-2", "Serie 2", "2")
        };

        var groups = CollectionQueryService.BuildReviewGroups(Catalog, photos);

        Assert.DoesNotContain(groups, group => group.IsCatchAll);
        Assert.Equal("2", groups[0].CardNumber);
    }

    [Fact]
    public void BuildReviewGroups_ReturnsTheGivenPhotoInstances()
    {
        var photo = Photo("p-2", "Serie 2", "2");
        var stray = Photo("p-stray", "", "");

        var groups = CollectionQueryService.BuildReviewGroups(Catalog, [photo, stray]);

        Assert.Same(stray, groups[0].Photos[0]);
        Assert.Same(photo, groups[1].Photos[0]);
    }

    [Fact]
    public void BuildReviewGroups_RegroupsAPhotoWhoseSeriesOrCardNumberChanged()
    {
        var original = Photo("p-1", "Serie 2", "2");
        var other = Photo("p-2", "Serie 2", "2");
        var before = CollectionQueryService.BuildReviewGroups(Catalog, [original, other]);
        Assert.Equal(2, Assert.Single(before).Photos.Count);

        var moved = original with { CardNumber = "10" };
        var after = CollectionQueryService.BuildReviewGroups(Catalog, [moved, other]);

        string?[] expected = ["2", "10"];
        Assert.Equal(expected, after.Select(group => group.CardNumber).ToArray());
        Assert.Same(other, Assert.Single(after[0].Photos));
        Assert.Same(moved, Assert.Single(after[1].Photos));
    }

    [Fact]
    public void BuildReviewGroups_MovesAPhotoWithABlankCardNumberToTheCatchAllGroup()
    {
        var photo = Photo("p-1", "Serie 2", "2") with { CardNumber = null };

        var groups = CollectionQueryService.BuildReviewGroups(Catalog, [photo]);

        Assert.True(Assert.Single(groups).IsCatchAll);
    }
}
