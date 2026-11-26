using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class PhotoUploadPreparerTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(100);

    private readonly CollectingLogger logger = new();
    private readonly List<(string Format, int MaxWidth, int MaxHeight)> resizeCalls = [];

    private PhotoUploadPreparer CreatePreparer(Func<IBrowserFile, string, int, int, Task<IBrowserFile>> resize, TimeSpan? timeout = null)
    {
        return new PhotoUploadPreparer(logger, timeout ?? ShortTimeout, (file, format, maxWidth, maxHeight) =>
        {
            resizeCalls.Add((format, maxWidth, maxHeight));
            return new ValueTask<IBrowserFile>(resize(file, format, maxWidth, maxHeight));
        });
    }

    private PhotoUploadPreparer CreatePreparer(IBrowserFile resized)
    {
        return CreatePreparer((_, _, _, _) => Task.FromResult(resized));
    }

    [Fact]
    public async Task Jpg_IsRequestedAsJpegWithLimit_AndSmallerResultIsReturned()
    {
        var original = new FakeBrowserFile("IMG_0001.jpg", 6_000_000);
        var resized = new FakeBrowserFile("IMG_0001.jpg", 400_000);

        var result = await CreatePreparer(resized).PrepareAsync(original);

        Assert.Same(resized, result);
        Assert.Equal([("image/jpeg", 1366, 1366)], resizeCalls);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData("IMG.JPG")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.JpEg")]
    public async Task JpegExtensions_AreCaseInsensitive(string name)
    {
        var original = new FakeBrowserFile(name, 6_000_000);
        var resized = new FakeBrowserFile(name, 400_000);

        var result = await CreatePreparer(resized).PrepareAsync(original);

        Assert.Same(resized, result);
        Assert.Equal([("image/jpeg", 1366, 1366)], resizeCalls);
    }

    [Theory]
    [InlineData("card.png")]
    [InlineData("card.PNG")]
    public async Task Png_IsRequestedAsPng(string name)
    {
        var original = new FakeBrowserFile(name, 6_000_000, "image/png");
        var resized = new FakeBrowserFile(name, 900_000, "image/png");

        var result = await CreatePreparer(resized).PrepareAsync(original);

        Assert.Same(resized, result);
        Assert.Equal([("image/png", 1366, 1366)], resizeCalls);
    }

    [Theory]
    [InlineData("card.webp")]
    [InlineData("card.bmp")]
    [InlineData("card.gif")]
    [InlineData("card.heic")]
    [InlineData("card")]
    public async Task OtherTypes_AreReturnedUnchanged_WithoutResizeAttempt(string name)
    {
        var original = new FakeBrowserFile(name, 6_000_000);

        var result = await CreatePreparer(new FakeBrowserFile(name, 1)).PrepareAsync(original);

        Assert.Same(original, result);
        Assert.Empty(resizeCalls);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(100_000)] // resized is as large as the original
    [InlineData(150_000)] // resized is larger
    public async Task ResizedFileNotSmaller_ReturnsOriginal_AndLogsWarning(long resizedSize)
    {
        var original = new FakeBrowserFile("small.jpg", 100_000);
        var resized = new FakeBrowserFile("small.jpg", resizedSize);

        var result = await CreatePreparer(resized).PrepareAsync(original);

        Assert.Same(original, result);
        AssertSingleWarning("small.jpg");
    }

    [Fact]
    public async Task EmptyResizedFile_ReturnsOriginal_AndLogsWarning()
    {
        var original = new FakeBrowserFile("IMG_0002.jpg", 6_000_000);
        var resized = new FakeBrowserFile("IMG_0002.jpg", 0);

        var result = await CreatePreparer(resized).PrepareAsync(original);

        Assert.Same(original, result);
        AssertSingleWarning("IMG_0002.jpg");
    }

    [Fact]
    public async Task JsException_ReturnsOriginal_AndLogsWarning()
    {
        var original = new FakeBrowserFile("IMG_0003.jpg", 6_000_000);

        var result = await CreatePreparer((_, _, _, _) => throw new JSException("decode failed")).PrepareAsync(original);

        Assert.Same(original, result);
        AssertSingleWarning("IMG_0003.jpg");
    }

    [Fact]
    public async Task InvalidOperationException_ReturnsOriginal_AndLogsWarning()
    {
        var original = new FakeBrowserFile("IMG_0004.jpg", 6_000_000);

        var result = await CreatePreparer((_, _, _, _) => Task.FromException<IBrowserFile>(new InvalidOperationException("boom"))).PrepareAsync(original);

        Assert.Same(original, result);
        AssertSingleWarning("IMG_0004.jpg");
    }

    [Fact]
    public async Task ResizeThatNeverCompletes_ReturnsOriginalAfterTimeout_AndLogsWarning()
    {
        var original = new FakeBrowserFile("IMG_0005.jpg", 6_000_000);
        var preparer = CreatePreparer((_, _, _, _) => new TaskCompletionSource<IBrowserFile>().Task);

        var result = await preparer.PrepareAsync(original).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Same(original, result);
        AssertSingleWarning("IMG_0005.jpg");
    }

    [Fact]
    public async Task CallerCancellation_Propagates_AndDoesNotFallBack()
    {
        var original = new FakeBrowserFile("IMG_0006.jpg", 6_000_000);
        var preparer = CreatePreparer((_, _, _, _) => new TaskCompletionSource<IBrowserFile>().Task, timeout: TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();

        var pending = preparer.PrepareAsync(original, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task BlazorsOwnResize_OnCustomBrowserFile_FallsBackToOriginal()
    {
        // The production default is Blazor's RequestImageFileAsync, which refuses IBrowserFile
        // implementations other than its own - that must end as a plain fallback.
        var original = new FakeBrowserFile("IMG_0007.jpg", 6_000_000);

        var result = await new PhotoUploadPreparer(logger).PrepareAsync(original);

        Assert.Same(original, result);
        AssertSingleWarning("IMG_0007.jpg");
    }

    private void AssertSingleWarning(string fileNameThatMustNotBeLogged)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain(fileNameThatMustNotBeLogged, entry.Message);
    }

    private sealed class CollectingLogger : ILogger<PhotoUploadPreparer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
