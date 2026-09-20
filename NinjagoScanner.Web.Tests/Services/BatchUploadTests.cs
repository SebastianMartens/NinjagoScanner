using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class BatchUploadTests
{
    private readonly List<string> uploadedNames = [];

    private BatchUploadFile File(string name, Func<Task>? behavior = null)
    {
        return new BatchUploadFile(name, async _ =>
        {
            if (behavior is not null)
            {
                await behavior();
            }

            uploadedNames.Add(name);
        });
    }

    private static HashSet<string> Existing(params string[] names) => new(names, StringComparer.Ordinal);

    [Fact]
    public async Task RunAsync_UploadsEveryNewFileInOrder()
    {
        var batch = new BatchUpload([File("a.jpg"), File("b.jpg"), File("c.jpg")], Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["a.jpg", "b.jpg", "c.jpg"], uploadedNames);
        Assert.Equal((3, 3, 3, 0, 0), (batch.Total, batch.Processed, batch.Uploaded, batch.Skipped, batch.Failed));
    }

    [Fact]
    public async Task RunAsync_SkipsAFileWhoseNameAlreadyExists()
    {
        var batch = new BatchUpload([File("IMG_0001.jpg"), File("IMG_0002.jpg")], Existing("IMG_0001.jpg"));

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["IMG_0002.jpg"], uploadedNames);
        Assert.Equal(["IMG_0001.jpg"], batch.SkippedNames);
        Assert.Equal((2, 1, 1, 0), (batch.Processed, batch.Uploaded, batch.Skipped, batch.Failed));
    }

    [Fact]
    public async Task RunAsync_SkipsASecondFileWithTheSameNameInTheSameBatch()
    {
        var batch = new BatchUpload([File("IMG_0002.jpg"), File("IMG_0002.jpg")], Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["IMG_0002.jpg"], uploadedNames);
        Assert.Equal(["IMG_0002.jpg"], batch.SkippedNames);
    }

    [Fact]
    public async Task RunAsync_NameMatchingIsCaseSensitive()
    {
        var batch = new BatchUpload([File("img_0003.jpg")], Existing("IMG_0003.jpg"));

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["img_0003.jpg"], uploadedNames);
        Assert.Equal(0, batch.Skipped);
    }

    [Fact]
    public async Task RunAsync_ValidationFailureIsRecordedAndTheBatchContinues()
    {
        var batch = new BatchUpload(
            [
                File("notes.txt", () => throw new InvalidOperationException("Dateityp wird nicht unterstuetzt.")),
                File("a.jpg")
            ],
            Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["a.jpg"], uploadedNames);
        var failure = Assert.Single(batch.FailedItems);
        Assert.Equal(new BatchUploadFailure("notes.txt", "Dateityp wird nicht unterstuetzt."), failure);
        Assert.Equal((2, 1, 0, 1), (batch.Processed, batch.Uploaded, batch.Skipped, batch.Failed));
    }

    [Fact]
    public async Task RunAsync_UploadExceptionIsRecordedWithItsMessageAndTheBatchContinues()
    {
        var batch = new BatchUpload(
            [File("a.jpg", () => throw new IOException("connection reset")), File("b.jpg")],
            Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["b.jpg"], uploadedNames);
        var failure = Assert.Single(batch.FailedItems);
        Assert.Equal("a.jpg", failure.FileName);
        Assert.Contains("connection reset", failure.Reason);
    }

    [Fact]
    public async Task RunAsync_AFailedFileDoesNotCountAsExistingSoARetryUploadsIt()
    {
        var existing = Existing();
        var failing = new BatchUpload([File("a.jpg", () => throw new IOException("boom"))], existing);
        await failing.RunAsync(CancellationToken.None);

        var retry = new BatchUpload([File("a.jpg")], existing);
        await retry.RunAsync(CancellationToken.None);

        Assert.Equal(["a.jpg"], uploadedNames);
        Assert.Equal(1, retry.Uploaded);
    }

    [Fact]
    public async Task RunAsync_CancellationStopsBeforeTheNextFile()
    {
        using var cancellation = new CancellationTokenSource();
        var batch = new BatchUpload(
            [File("a.jpg", () => { cancellation.Cancel(); return Task.CompletedTask; }), File("b.jpg")],
            Existing());

        await batch.RunAsync(cancellation.Token);

        Assert.Equal(["a.jpg"], uploadedNames);
        Assert.True(batch.WasCancelled);
        Assert.Equal((1, 1, 0), (batch.Processed, batch.Uploaded, batch.Failed));
    }

    [Fact]
    public async Task RunAsync_CancellationInsideAnUploadIsNotRecordedAsAFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var batch = new BatchUpload(
            [new BatchUploadFile("a.jpg", async token =>
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            })],
            Existing());

        await batch.RunAsync(cancellation.Token);

        Assert.True(batch.WasCancelled);
        Assert.Equal((0, 0, 0), (batch.Processed, batch.Uploaded, batch.Failed));
    }

    [Fact]
    public async Task RunAsync_UploadsOneFileAtATime()
    {
        var running = 0;
        var maxRunning = 0;
        BatchUploadFile Tracked(string name) => new(name, async _ =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Yield();
            Interlocked.Decrement(ref running);
        });

        var batch = new BatchUpload([Tracked("a.jpg"), Tracked("b.jpg"), Tracked("c.jpg")], Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task RunAsync_RaisesChangedOncePerProcessedFile()
    {
        var batch = new BatchUpload([File("a.jpg"), File("b.jpg")], Existing("b.jpg"));
        var processedAtEachChange = new List<int>();
        batch.Changed += () => processedAtEachChange.Add(batch.Processed);

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal([1, 2], processedAtEachChange);
    }

    [Fact]
    public async Task RunAsync_SingleFileBatchStillUploadsThroughTheBatchDelegate()
    {
        var batch = new BatchUpload([File("only.jpg")], Existing());

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(["only.jpg"], uploadedNames);
    }

    [Fact]
    public async Task RunAsync_RetryOfAnInterruptedBatch_SkipsTheAlreadyUploadedFilesAndUploadsTheRest()
    {
        var allNames = Enumerable.Range(1, 1000).Select(number => $"IMG_{number:0000}.jpg").ToArray();
        var alreadyUploaded = allNames.Take(400);
        var batch = new BatchUpload(allNames.Select(name => File(name)).ToArray(), Existing([.. alreadyUploaded]));

        await batch.RunAsync(CancellationToken.None);

        Assert.Equal(400, batch.Skipped);
        Assert.Equal(600, batch.Uploaded);
        Assert.Equal(1000, batch.Processed);
        Assert.Equal(allNames.Skip(400), uploadedNames);
    }
}
