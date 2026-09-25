using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class ReviewSessionTests
{
    private static readonly IReadOnlyList<(string Series, string Category, string CardNumber, string CardName, int SortOrder)> Catalog =
    [
        ("Serie 2", "Good Guys", "2", "Two", 2),
        ("Serie 2", "Good Guys", "10", "Ten", 2),
        ("Serie 10", "Good Guys", "1", "Kai", 10)
    ];

    private static CardListItem Photo(
        string photoId,
        string? setName,
        string? cardNumber,
        string reviewStatus = ReviewStatuses.Unreviewed) => new()
    {
        PhotoId = photoId,
        SourceFileName = photoId,
        ImageUrl = $"https://example.test/{photoId}?sig=abc",
        AnalysisStatus = AnalysisStatuses.Ok,
        ReviewStatus = reviewStatus,
        SetName = setName,
        CardNumber = cardNumber,
        Language = Languages.German
    };

    private static ReviewSession Session(params CardListItem[] photos) =>
        new(new ReviewSnapshot(Catalog, photos));

    private static ReviewSession SessionShowingAllGroups(params CardListItem[] photos)
    {
        var session = Session(photos);
        session.SetReviewStatusFilter(ReviewSession.AllFilterValue);
        return session;
    }

    // --- Defaults and mutation basics ---

    [Fact]
    public void ReviewStatusFilter_DefaultsToUnreviewed()
    {
        Assert.Equal(ReviewStatuses.Unreviewed, Session().ReviewStatusFilter);
    }

    [Fact]
    public void NewSession_StartsOnTheCatchAllGroup_AndNextMovesToTheFirstMatchedGroup()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-stray", "Unknown Series", "1"));

        Assert.True(session.CurrentGroup!.IsCatchAll);

        session.GoToNext();

        Assert.False(session.CurrentGroup!.IsCatchAll);
        Assert.Equal("2", session.CurrentGroup.CardNumber);
    }

    [Fact]
    public void ReplacePhoto_LeavesEveryOtherPhotoInstanceUntouched()
    {
        var changed = Photo("p-1", "Serie 2", "2");
        var other = Photo("p-2", "Serie 2", "2");
        var elsewhere = Photo("p-3", "Serie 10", "1");
        var session = SessionShowingAllGroups(changed, other, elsewhere);

        session.ReplacePhoto("p-1", photo => ReviewSession.WithReviewStatus(photo, ReviewStatuses.Verified));

        var twos = session.Groups[0].Photos;
        Assert.Equal(ReviewStatuses.Verified, twos.Single(photo => photo.PhotoId == "p-1").ReviewStatus);
        Assert.Same(other, twos.Single(photo => photo.PhotoId == "p-2"));
        Assert.Same(elsewhere, session.Groups[1].Photos.Single());
    }

    [Fact]
    public void ReplacePhoto_KeepsTheChangedPhotosImageUrl()
    {
        var photo = Photo("p-1", "Serie 2", "2");
        var session = SessionShowingAllGroups(photo);

        session.ReplacePhoto("p-1", current => ReviewSession.WithCardNumber(current, "10"));

        Assert.Equal(photo.ImageUrl, session.Groups.Single().Photos.Single().ImageUrl);
    }

    [Fact]
    public void ReplacePhoto_IgnoresAnUnknownPhotoId()
    {
        var photo = Photo("p-1", "Serie 2", "2");
        var session = SessionShowingAllGroups(photo);

        session.ReplacePhoto("nope", current => current with { ReviewStatus = ReviewStatuses.Incorrect });

        Assert.Same(photo, session.Groups.Single().Photos.Single());
    }

    // --- Showing a specific card ---

    [Fact]
    public void TryShowCard_ShowsAFullyVerifiedGroupAndClearsEveryFilter()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10", ReviewStatuses.Verified));
        session.SetAnalysisStatusFilter(AnalysisStatuses.Failed);
        session.SetSearchText("nothing");

        var shown = session.TryShowCard("Serie 2", "10");

        Assert.True(shown);
        Assert.Equal("10", session.CurrentGroup!.CardNumber);
        Assert.Equal(ReviewSession.AllFilterValue, session.ReviewStatusFilter);
        Assert.Equal(ReviewSession.AllFilterValue, session.AnalysisStatusFilter);
        Assert.Equal(string.Empty, session.SearchText);
    }

    [Fact]
    public void TryShowCard_MatchesLikeTheGroupingDoes()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));

        Assert.True(session.TryShowCard(" serie 2 ", "010"));

        Assert.Equal("10", session.CurrentGroup!.CardNumber);
    }

    [Fact]
    public void TryShowCard_KeepsAllPhotosOfTheGroup()
    {
        var session = Session(
            Photo("p-a", "Serie 2", "2", ReviewStatuses.Verified),
            Photo("p-b", "Serie 2", "2", ReviewStatuses.Incorrect));

        session.TryShowCard("Serie 2", "2");

        Assert.Equal(2, session.CurrentGroup!.Photos.Count);
    }

    [Fact]
    public void TryShowCard_ThenNextAndPrevious_WalkTheClearedGroupList()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2", ReviewStatuses.Verified),
            Photo("p-10", "Serie 2", "10", ReviewStatuses.Verified),
            Photo("p-kai", "Serie 10", "1", ReviewStatuses.Verified));

        session.TryShowCard("Serie 2", "10");
        session.GoToNext();
        Assert.Equal("Serie 10", session.CurrentGroup!.SeriesName);

        session.GoToPrevious();
        session.GoToPrevious();
        Assert.Equal("2", session.CurrentGroup!.CardNumber);
    }

    [Theory]
    [InlineData("Serie 2", "10")]   // catalog card without any photo
    [InlineData("Serie 99", "1")]   // unknown series
    [InlineData("Serie 2", null)]
    [InlineData(null, "2")]
    [InlineData("", "")]
    public void TryShowCard_WithoutAMatchingGroup_ChangesNothing(string? series, string? card)
    {
        var session = Session(Photo("p-2", "Serie 2", "2"));

        var shown = session.TryShowCard(series, card);

        Assert.False(shown);
        Assert.Equal(ReviewStatuses.Unreviewed, session.ReviewStatusFilter);
        Assert.Equal(0, session.CurrentIndex);
    }

    // --- Position rules ---

    [Fact]
    public void StatusChange_KeepsTheCurrentGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"),
            Photo("p-kai", "Serie 10", "1"));
        session.GoToNext();
        var shown = session.CurrentGroup!.Key;

        session.ReplacePhoto("p-10", photo => ReviewSession.WithReviewStatus(photo, ReviewStatuses.Verified));

        Assert.Equal(shown, session.CurrentGroup!.Key);
        Assert.Equal(1, session.CurrentIndex);
    }

    [Fact]
    public void SeriesOrNumberChange_MovesThePhotoOutOfTheCurrentGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-a", "Serie 2", "2"),
            Photo("p-b", "Serie 2", "2"));
        var shown = session.CurrentGroup!.Key;

        session.ReplacePhoto("p-a", photo => ReviewSession.WithCardNumber(photo, "10"));

        Assert.Equal(shown, session.CurrentGroup!.Key);
        Assert.Equal("p-b", Assert.Single(session.CurrentGroup.Photos).PhotoId);
        Assert.Equal("p-a", Assert.Single(session.Groups[1].Photos).PhotoId);
    }

    [Fact]
    public void RemovingTheLastPhotoOfTheCurrentGroup_AdvancesToTheNextGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));

        session.RemovePhoto("p-2");

        Assert.Equal("10", session.CurrentGroup!.CardNumber);
        Assert.Equal(0, session.CurrentIndex);
    }

    [Fact]
    public void RemovingTheLastPhotoOfTheLastGroup_MovesBackToTheNearestGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));
        session.GoToNext();

        session.RemovePhoto("p-10");

        Assert.Equal("2", session.CurrentGroup!.CardNumber);
    }

    [Fact]
    public void RemovingOneOfSeveralPhotos_StaysOnTheSameGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-a", "Serie 2", "2"),
            Photo("p-b", "Serie 2", "2"));

        session.RemovePhoto("p-a");

        Assert.Equal("p-b", Assert.Single(session.CurrentGroup!.Photos).PhotoId);
    }

    [Fact]
    public void RemovingTheLastRemainingPhoto_LeavesNothingToShow()
    {
        var session = SessionShowingAllGroups(Photo("p-2", "Serie 2", "2"));

        session.RemovePhoto("p-2");

        Assert.Empty(session.FilteredGroups);
        Assert.Null(session.CurrentGroup);
    }

    [Fact]
    public void VerifyingTheLastUnreviewedPhotoUnderTheUnreviewedFilter_DropsTheGroupAndShowsTheNextOne()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));

        session.ReplacePhoto("p-2", photo => ReviewSession.WithReviewStatus(photo, ReviewStatuses.Verified));

        Assert.Equal("10", session.CurrentGroup!.CardNumber);
    }

    [Fact]
    public void ChangingAFilter_ReturnsToTheFirstMatchingGroup()
    {
        var session = SessionShowingAllGroups(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));
        session.GoToNext();

        session.SetSearchText("ten");

        Assert.Equal(0, session.CurrentIndex);
    }

    // --- Cached filtered list ---

    [Fact]
    public void FilteredGroups_IsTheSameListUntilGroupsOrFiltersChange()
    {
        var session = SessionShowingAllGroups(Photo("p-2", "Serie 2", "2"));

        var first = session.FilteredGroups;
        Assert.Same(first, session.FilteredGroups);

        session.ReplacePhoto("p-2", photo => ReviewSession.WithReviewStatus(photo, ReviewStatuses.Incorrect));
        var afterMutation = session.FilteredGroups;
        Assert.NotSame(first, afterMutation);
        Assert.Same(afterMutation, session.FilteredGroups);

        session.SetAnalysisStatusFilter(AnalysisStatuses.Ok);
        Assert.NotSame(afterMutation, session.FilteredGroups);
    }

    // --- Local normalization matches what a reload would show ---

    [Theory]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData(" 04 ", "04")]
    public void WithCardNumber_NormalizesLikeTheServerRoundTrip(string? sent, string? expected)
    {
        var photo = ReviewSession.WithCardNumber(Photo("p-1", "Serie 2", "2"), sent);

        Assert.Equal(expected, photo.CardNumber);
    }

    [Theory]
    [InlineData("  ", null)]
    [InlineData(" Serie 10 ", "Serie 10")]
    public void WithSetName_NormalizesLikeTheServerRoundTrip(string? sent, string? expected)
    {
        var photo = ReviewSession.WithSetName(Photo("p-1", "Serie 2", "2"), sent);

        Assert.Equal(expected, photo.SetName);
    }

    [Fact]
    public void WithLanguage_FallsBackToTheDefaultWhenBlank()
    {
        var photo = ReviewSession.WithLanguage(Photo("p-1", "Serie 2", "2"), " ");

        Assert.Equal(Languages.Default, photo.Language);
    }

    // --- Re-analysis ---

    [Fact]
    public void ReanalysisThatChangesTheCardNumber_MovesThePhotoAndKeepsItsImageUrl()
    {
        var photo = Photo("p-1", "Serie 2", "2");
        var session = SessionShowingAllGroups(photo, Photo("p-other", "Serie 2", "10"));
        var reanalyzed = photo with { CardNumber = "10", ImageUrl = string.Empty };

        session.ReplacePhoto("p-1", previous => reanalyzed with { ImageUrl = previous.ImageUrl });

        var tenGroup = session.Groups.Single(group => group.CardNumber == "10");
        var moved = tenGroup.Photos.Single(candidate => candidate.PhotoId == "p-1");
        Assert.Equal(photo.ImageUrl, moved.ImageUrl);
        Assert.DoesNotContain(session.Groups, group => group.CardNumber == "2");
    }

    // --- Confirm all ---

    private sealed class RecordingSaver(int? failOnCall = null)
    {
        public List<string> Calls { get; } = [];

        public Task SaveAsync(string photoId)
        {
            Calls.Add(photoId);
            if (failOnCall == Calls.Count)
            {
                throw new InvalidOperationException("save failed");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ConfirmAsync_SavesOnlyPhotosThatAreNotYetVerified()
    {
        var session = Session(
            Photo("p-verified", "Serie 2", "2", ReviewStatuses.Verified),
            Photo("p-unreviewed", "Serie 2", "2"),
            Photo("p-incorrect", "Serie 2", "2", ReviewStatuses.Incorrect),
            Photo("p-next", "Serie 2", "10"));
        var saver = new RecordingSaver();

        await session.ConfirmAsync(session.CurrentGroup!.Photos, saver.SaveAsync);

        Assert.Equal(["p-incorrect", "p-unreviewed"], saver.Calls.Order().ToArray());
        Assert.All(session.Groups[0].Photos, photo => Assert.Equal(ReviewStatuses.Verified, photo.ReviewStatus));
    }

    [Fact]
    public async Task ConfirmAsync_AdvancesToTheNextGroupMatchingTheFilter()
    {
        var session = Session(
            Photo("p-2", "Serie 2", "2"),
            Photo("p-10", "Serie 2", "10"));

        await session.ConfirmAsync(session.CurrentGroup!.Photos, new RecordingSaver().SaveAsync);

        Assert.Equal("10", session.CurrentGroup!.CardNumber);
        Assert.Equal(0, session.CurrentIndex);
    }

    [Fact]
    public async Task ConfirmAsync_OnAnAlreadyVerifiedGroup_SavesNothingAndStillAdvances()
    {
        var session = SessionShowingAllGroups(
            Photo("p-2", "Serie 2", "2", ReviewStatuses.Verified),
            Photo("p-10", "Serie 2", "10", ReviewStatuses.Verified));
        var saver = new RecordingSaver();

        await session.ConfirmAsync(session.CurrentGroup!.Photos, saver.SaveAsync);

        Assert.Empty(saver.Calls);
        Assert.Equal("10", session.CurrentGroup!.CardNumber);
    }

    [Fact]
    public async Task ConfirmAsync_UnderTheUnreviewedFilter_ShowsTheEmptyStateAfterTheLastMatchingGroup()
    {
        var session = Session(Photo("p-2", "Serie 2", "2"));

        await session.ConfirmAsync(session.CurrentGroup!.Photos, new RecordingSaver().SaveAsync);

        Assert.Empty(session.FilteredGroups);
        Assert.Null(session.CurrentGroup);
    }

    [Fact]
    public async Task ConfirmAsync_LeavesCandidatesOutsideTheDisplayedSubsetUntouched()
    {
        var shown = Photo("p-a", "Serie 2", "2");
        var hidden = Photo("p-b", "Serie 2", "2");
        var session = SessionShowingAllGroups(shown, hidden);
        var saver = new RecordingSaver();

        await session.ConfirmAsync([shown], saver.SaveAsync);

        Assert.Equal(["p-a"], saver.Calls);
        var group = session.Groups.Single();
        Assert.Equal(ReviewStatuses.Verified, group.Photos.Single(photo => photo.PhotoId == "p-a").ReviewStatus);
        Assert.Same(hidden, group.Photos.Single(photo => photo.PhotoId == "p-b"));
    }

    [Fact]
    public async Task ConfirmAsync_WhenASaveFails_KeepsEarlierPhotosVerifiedAndTheRestUnchanged()
    {
        var session = SessionShowingAllGroups(
            Photo("p-1", "Serie 2", "2"),
            Photo("p-2", "Serie 2", "2"),
            Photo("p-3", "Serie 2", "2"),
            Photo("p-next", "Serie 2", "10"));
        var shown = session.CurrentGroup!.Key;
        var saver = new RecordingSaver(failOnCall: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConfirmAsync(session.CurrentGroup!.Photos, saver.SaveAsync));

        Assert.Equal(["p-1", "p-2"], saver.Calls);
        Assert.Equal(shown, session.CurrentGroup!.Key);
        var statuses = session.CurrentGroup.Photos.ToDictionary(photo => photo.PhotoId, photo => photo.ReviewStatus);
        Assert.Equal(ReviewStatuses.Verified, statuses["p-1"]);
        Assert.Equal(ReviewStatuses.Unreviewed, statuses["p-2"]);
        Assert.Equal(ReviewStatuses.Unreviewed, statuses["p-3"]);
    }

    // --- Download URL resolution (scope-photo-download-urls) ---

    private const int DisplayCap = 18;

    private static CardListItem WithoutUrl(CardListItem photo) => photo with { ImageUrl = string.Empty };

    private sealed class RecordingUrlResolver
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<IReadOnlyDictionary<string, string>> ResolveAsync(IReadOnlyList<string> photoIds)
        {
            Calls.Add(photoIds);
            IReadOnlyDictionary<string, string> urls = photoIds.ToDictionary(id => id, id => $"https://resolved.test/{id}");
            return Task.FromResult(urls);
        }
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_ResolvesOnlyTheCurrentGroupsPhotos_InOneCall()
    {
        var session = SessionShowingAllGroups(
            WithoutUrl(Photo("p-1", "Serie 2", "2")),
            WithoutUrl(Photo("p-2", "Serie 2", "2")),
            WithoutUrl(Photo("p-other-group", "Serie 2", "10")));
        var resolver = new RecordingUrlResolver();

        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);

        var call = Assert.Single(resolver.Calls);
        Assert.Equal(["p-1", "p-2"], call.Order());
        Assert.All(session.CurrentGroup!.Photos, photo => Assert.Equal($"https://resolved.test/{photo.PhotoId}", photo.ImageUrl));
        var otherGroupPhoto = session.Groups.Single(group => group.Photos.Any(photo => photo.PhotoId == "p-other-group")).Photos.Single();
        Assert.Equal(string.Empty, otherGroupPhoto.ImageUrl);
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_NeverRequestsMoreThanTheDisplayCap()
    {
        var photos = Enumerable.Range(1, 25).Select(i => WithoutUrl(Photo($"p-{i:00}", "Serie 2", "2"))).ToArray();
        var session = SessionShowingAllGroups(photos);
        var resolver = new RecordingUrlResolver();

        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);

        var call = Assert.Single(resolver.Calls);
        Assert.Equal(DisplayCap, call.Count);
        Assert.DoesNotContain("p-25", call);
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_MakesNoCallWhenEveryDisplayedPhotoAlreadyHasAUrl()
    {
        var session = SessionShowingAllGroups(Photo("p-1", "Serie 2", "2"), Photo("p-2", "Serie 2", "2"));
        var resolver = new RecordingUrlResolver();

        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);

        Assert.Empty(resolver.Calls);
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_NavigatingToAnotherGroup_ResolvesOnlyThatGroup_AndBackDoesNotRerequest()
    {
        var session = SessionShowingAllGroups(
            WithoutUrl(Photo("p-2", "Serie 2", "2")),
            WithoutUrl(Photo("p-10", "Serie 2", "10")));
        var resolver = new RecordingUrlResolver();

        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);
        session.GoToNext();
        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);
        session.GoToPrevious();
        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);

        Assert.Equal(2, resolver.Calls.Count);
        Assert.Equal(["p-2"], resolver.Calls[0]);
        Assert.Equal(["p-10"], resolver.Calls[1]);
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_APhotoEnteringTheGroupViaALocalEdit_GetsItsUrl_WithoutRerequestingTheOthers()
    {
        var session = SessionShowingAllGroups(
            WithoutUrl(Photo("p-1", "Serie 2", "2")),
            WithoutUrl(Photo("p-stray", "Serie 10", "1")));
        var resolver = new RecordingUrlResolver();
        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);
        var shownKey = session.CurrentGroup!.Key;
        Assert.Equal(["p-1"], Assert.Single(resolver.Calls));

        // p-stray was never in a displayed group; a series/number correction moves it into this one.
        session.ReplacePhoto("p-stray", photo => photo with { SetName = "Serie 2", CardNumber = "2" });
        Assert.Equal(shownKey, session.CurrentGroup!.Key);
        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, resolver.ResolveAsync);

        Assert.Equal(2, resolver.Calls.Count);
        Assert.Equal(["p-stray"], resolver.Calls[1]);
        Assert.All(session.CurrentGroup.Photos, photo => Assert.False(string.IsNullOrEmpty(photo.ImageUrl)));
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_NeverOverwritesAnExistingUrl()
    {
        var session = SessionShowingAllGroups(Photo("p-1", "Serie 2", "2"), WithoutUrl(Photo("p-2", "Serie 2", "2")));
        var originalUrl = session.CurrentGroup!.Photos.Single(photo => photo.PhotoId == "p-1").ImageUrl;

        // A resolver that (wrongly) also returns a fresh URL for the photo that already has one.
        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, _ =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
            {
                ["p-1"] = "https://other.test/p-1",
                ["p-2"] = "https://other.test/p-2"
            }));

        var photos = session.CurrentGroup.Photos.ToDictionary(photo => photo.PhotoId);
        Assert.Equal(originalUrl, photos["p-1"].ImageUrl);
        Assert.Equal("https://other.test/p-2", photos["p-2"].ImageUrl);
    }

    [Fact]
    public async Task ResolveMissingDownloadUrls_APhotoTheResolverReturnsNoUrlFor_StaysWithoutOne()
    {
        var session = SessionShowingAllGroups(WithoutUrl(Photo("p-1", "Serie 2", "2")), WithoutUrl(Photo("p-gone", "Serie 2", "2")));

        await session.ResolveMissingDownloadUrlsAsync(DisplayCap, _ =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { ["p-1"] = "https://resolved.test/p-1" }));

        var photos = session.CurrentGroup!.Photos.ToDictionary(photo => photo.PhotoId);
        Assert.Equal("https://resolved.test/p-1", photos["p-1"].ImageUrl);
        Assert.Equal(string.Empty, photos["p-gone"].ImageUrl);
    }
}
