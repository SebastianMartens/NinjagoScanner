namespace NinjagoScanner.Web.Services;

/// <summary>One file selected for a batch upload: its name (what skip-if-filename-exists matches on) and how to upload it.</summary>
internal sealed record BatchUploadFile(string Name, Func<CancellationToken, Task> UploadAsync);

internal sealed record BatchUploadFailure(string FileName, string Reason);

/// <summary>
/// The sequential upload loop behind the /upload page's batch input, kept out of the .razor code
/// block so it can be tested. Files are processed strictly one at a time; a file whose exact
/// (ordinal, case-sensitive) name is already in the existing-names set is skipped, and each
/// uploaded name joins that set so a same-named file later in the batch is skipped too. A file
/// that fails is recorded with its reason and never aborts the batch. Holds the running counters
/// and lists the page renders, and raises <see cref="Changed"/> after every processed file (the
/// page decides how often to actually re-render).
/// </summary>
internal sealed class BatchUpload
{
    private readonly IReadOnlyList<BatchUploadFile> files;
    private readonly ISet<string> existingNames;
    private readonly List<string> skippedNames = [];
    private readonly List<BatchUploadFailure> failedItems = [];

    public BatchUpload(IReadOnlyList<BatchUploadFile> files, ISet<string> existingNames)
    {
        this.files = files;
        this.existingNames = existingNames;
    }

    public int Total => files.Count;
    public int Processed { get; private set; }
    public int Uploaded { get; private set; }
    public int Skipped => skippedNames.Count;
    public int Failed => failedItems.Count;
    public IReadOnlyList<string> SkippedNames => skippedNames;
    public IReadOnlyList<BatchUploadFailure> FailedItems => failedItems;

    /// <summary>True when the run stopped early because its cancellation token was cancelled.</summary>
    public bool WasCancelled { get; private set; }

    public event Action? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                return;
            }

            if (existingNames.Contains(file.Name))
            {
                skippedNames.Add(file.Name);
            }
            else
            {
                try
                {
                    await file.UploadAsync(cancellationToken);
                    existingNames.Add(file.Name);
                    Uploaded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    WasCancelled = true;
                    return;
                }
                catch (InvalidOperationException exception)
                {
                    failedItems.Add(new BatchUploadFailure(file.Name, exception.Message));
                }
                catch (Exception exception)
                {
                    failedItems.Add(new BatchUploadFailure(file.Name, $"Upload fehlgeschlagen: {exception.Message}"));
                }
            }

            Processed++;
            Changed?.Invoke();
        }
    }
}
