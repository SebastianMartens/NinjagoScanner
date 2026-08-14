using System.Reflection;
using NinjagoScanner.Web.Components.Pages;
using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Tests.Components;

public sealed class ReviewFilterTests
{
    [Fact]
    public void ReviewStatusFilter_DefaultsToUnreviewed()
    {
        var field = typeof(Review).GetField("reviewStatusFilter", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var defaultValue = (string?)field.GetValue(new Review());

        Assert.Equal(ReviewStatuses.Unreviewed, defaultValue);
    }

    private static CardListItem Photo(string imageFileName, string reviewStatus = ReviewStatuses.Unreviewed, string analysisStatus = AnalysisStatuses.Ok, string? cardName = null, string? cardNumber = null) => new()
    {
        ImageFileName = imageFileName,
        ImageUrl = $"/images/{imageFileName}",
        AnalysisStatus = analysisStatus,
        ReviewStatus = reviewStatus,
        CardName = cardName,
        CardNumber = cardNumber
    };

    private static CardReviewGroup Group(params CardListItem[] photos) => new()
    {
        IsCatchAll = false,
        SeriesName = "Serie 2",
        CardNumber = "4",
        CardName = "Cole",
        Photos = photos
    };

    [Fact]
    public void MatchesReviewStatusFilter_All_MatchesEveryGroup()
    {
        var group = Group(Photo("a.jpg", reviewStatus: ReviewStatuses.Incorrect));

        Assert.True(Review.MatchesReviewStatusFilter(group, "all"));
    }

    [Fact]
    public void MatchesReviewStatusFilter_MatchesWhenAnyPhotoHasStatus()
    {
        var group = Group(
            Photo("a.jpg", reviewStatus: ReviewStatuses.Unreviewed),
            Photo("b.jpg", reviewStatus: ReviewStatuses.Verified));

        Assert.True(Review.MatchesReviewStatusFilter(group, ReviewStatuses.Verified));
    }

    [Fact]
    public void MatchesReviewStatusFilter_NoMatch_WhenNoPhotoHasStatus()
    {
        var group = Group(Photo("a.jpg", reviewStatus: ReviewStatuses.Unreviewed));

        Assert.False(Review.MatchesReviewStatusFilter(group, ReviewStatuses.Incorrect));
    }

    [Fact]
    public void MatchesAnalysisStatusFilter_All_MatchesEveryGroup()
    {
        var group = Group(Photo("a.jpg", analysisStatus: AnalysisStatuses.Failed));

        Assert.True(Review.MatchesAnalysisStatusFilter(group, "all"));
    }

    [Fact]
    public void MatchesAnalysisStatusFilter_MatchesWhenAnyPhotoHasStatus()
    {
        var group = Group(
            Photo("a.jpg", analysisStatus: AnalysisStatuses.Ok),
            Photo("b.jpg", analysisStatus: AnalysisStatuses.Uncertain));

        Assert.True(Review.MatchesAnalysisStatusFilter(group, AnalysisStatuses.Uncertain));
    }

    [Fact]
    public void MatchesAnalysisStatusFilter_NoMatch_WhenNoPhotoHasStatus()
    {
        var group = Group(Photo("a.jpg", analysisStatus: AnalysisStatuses.Ok));

        Assert.False(Review.MatchesAnalysisStatusFilter(group, AnalysisStatuses.Pending));
    }

    [Fact]
    public void MatchesSearchFilter_EmptySearch_MatchesEveryGroup()
    {
        var group = Group(Photo("a.jpg", cardName: "Kai", cardNumber: "4"));

        Assert.True(Review.MatchesSearchFilter(group, string.Empty));
    }

    [Fact]
    public void MatchesSearchFilter_MatchesByCardName_CaseInsensitive()
    {
        var group = Group(Photo("a.jpg", cardName: "Ultra Zane", cardNumber: "10"));

        Assert.True(Review.MatchesSearchFilter(group, "zane"));
    }

    [Fact]
    public void MatchesSearchFilter_MatchesByCardNumber_Substring()
    {
        var group = Group(Photo("a.jpg", cardName: "Kai", cardNumber: "LE12"));

        Assert.True(Review.MatchesSearchFilter(group, "LE1"));
    }

    [Fact]
    public void MatchesSearchFilter_NoMatch_WhenNoPhotoContainsSearchText()
    {
        var group = Group(Photo("a.jpg", cardName: "Kai", cardNumber: "4"));

        Assert.False(Review.MatchesSearchFilter(group, "zane"));
    }

    [Fact]
    public void MatchesFilters_CombinesAllThreeFiltersWithAnd()
    {
        var group = Group(
            Photo("a.jpg", reviewStatus: ReviewStatuses.Unreviewed, analysisStatus: AnalysisStatuses.Uncertain, cardName: "Kai", cardNumber: "4"),
            Photo("b.jpg", reviewStatus: ReviewStatuses.Verified, analysisStatus: AnalysisStatuses.Ok, cardName: "Zane", cardNumber: "10"));

        // No single photo is both Unreviewed AND Uncertain AND matches "zane" - but each
        // filter independently matches at least one photo in the group, so all three pass.
        Assert.True(Review.MatchesFilters(group, ReviewStatuses.Unreviewed, AnalysisStatuses.Ok, "zane"));

        // No photo has AnalysisStatus "failed", so the combined filter excludes the group.
        Assert.False(Review.MatchesFilters(group, "all", AnalysisStatuses.Failed, string.Empty));
    }
}
