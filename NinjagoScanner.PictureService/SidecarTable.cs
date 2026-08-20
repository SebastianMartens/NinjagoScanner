using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

namespace NinjagoScanner.PictureService;

/// <summary>
/// Storage seam for sidecar records, keyed by generated photo ID. Implemented against DynamoDB
/// by <see cref="SidecarTable"/>; tests substitute an in-memory fake instead of mocking the AWS SDK.
/// </summary>
internal interface ISidecarStore
{
    Task<SidecarRecord?> GetAsync(string photoId, CancellationToken cancellationToken);

    Task PutAsync(string photoId, SidecarRecord record, CancellationToken cancellationToken);

    Task DeleteAsync(string photoId, CancellationToken cancellationToken);

    IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListAllAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads and writes sidecar records in the DynamoDB table configured via
/// <see cref="ScannerConfig.ResolveSidecarTableName"/>, keyed by generated photo ID
/// (see <see cref="PhotoStore"/> for the matching S3 object identity).
/// </summary>
internal sealed class SidecarTable : ISidecarStore
{
    private const string PhotoIdAttribute = "PhotoId";

    private readonly ITable table;

    public SidecarTable(IAmazonDynamoDB dynamoDb, string tableName)
    {
        table = new TableBuilder(dynamoDb, tableName)
            .AddHashKey(PhotoIdAttribute, DynamoDBEntryType.String)
            .Build();
    }

    public async Task<SidecarRecord?> GetAsync(string photoId, CancellationToken cancellationToken)
    {
        var document = await table.GetItemAsync(photoId, cancellationToken);
        return document is null ? null : FromDocument(document);
    }

    public async Task PutAsync(string photoId, SidecarRecord record, CancellationToken cancellationToken)
    {
        var document = ToDocument(photoId, record);
        await table.PutItemAsync(document, cancellationToken);
    }

    public async Task DeleteAsync(string photoId, CancellationToken cancellationToken)
    {
        await table.DeleteItemAsync(photoId, cancellationToken);
    }

    /// <summary>
    /// Scans the whole table. Sidecar counts are small enough (thousands, not millions) for a
    /// full table scan to be an acceptable read pattern, mirroring the previous
    /// read-every-sidecar-file behavior.
    /// </summary>
    public async IAsyncEnumerable<(string PhotoId, SidecarRecord Record)> ListAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var search = table.Scan(new ScanOperationConfig());
        while (!search.IsDone)
        {
            var page = await search.GetNextSetAsync(cancellationToken);
            foreach (var document in page)
            {
                var photoId = document[PhotoIdAttribute].AsString();
                yield return (photoId, FromDocument(document));
            }
        }
    }

    private static Document ToDocument(string photoId, SidecarRecord record)
    {
        var document = new Document
        {
            [PhotoIdAttribute] = photoId
        };

        SetIfPresent(document, nameof(SidecarRecord.AnalysisStatus), record.AnalysisStatus);
        SetIfPresent(document, nameof(SidecarRecord.ReviewStatus), record.ReviewStatus);
        SetIfPresent(document, nameof(SidecarRecord.CardName), record.CardName);
        SetIfPresent(document, nameof(SidecarRecord.CardNumber), record.CardNumber);
        SetIfPresent(document, nameof(SidecarRecord.SetName), record.SetName);
        SetIfPresent(document, nameof(SidecarRecord.Rarity), record.Rarity);
        SetIfPresent(document, nameof(SidecarRecord.Language), record.Language);
        document[nameof(SidecarRecord.Confidence)] = record.Confidence;
        SetIfPresent(document, nameof(SidecarRecord.ReasoningSummary), record.ReasoningSummary);
        if (record.DetectedText is { Length: > 0 })
        {
            // Explicit DynamoDBList (not the default Set conversion for string[]) because
            // OCR-detected text legitimately contains duplicate entries, which a Set rejects.
            document[nameof(SidecarRecord.DetectedText)] = (DynamoDBList)record.DetectedText;
        }
        if (record.ScannedAtUtc is { } scannedAt)
        {
            document[nameof(SidecarRecord.ScannedAtUtc)] = scannedAt.ToString("o");
        }
        SetIfPresent(document, nameof(SidecarRecord.ErrorMessage), record.ErrorMessage);
        SetIfPresent(document, nameof(SidecarRecord.SourceFileName), record.SourceFileName);
        SetIfPresent(document, nameof(SidecarRecord.AiModel), record.AiModel);
        SetIfPresent(document, nameof(SidecarRecord.RawModelResponse), record.RawModelResponse);

        return document;
    }

    private static void SetIfPresent(Document document, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            document[key] = value;
        }
    }

    private static SidecarRecord FromDocument(Document document)
    {
        return new SidecarRecord
        {
            AnalysisStatus = GetString(document, nameof(SidecarRecord.AnalysisStatus)),
            ReviewStatus = GetString(document, nameof(SidecarRecord.ReviewStatus)),
            CardName = GetString(document, nameof(SidecarRecord.CardName)),
            CardNumber = GetString(document, nameof(SidecarRecord.CardNumber)),
            SetName = GetString(document, nameof(SidecarRecord.SetName)),
            Rarity = GetString(document, nameof(SidecarRecord.Rarity)),
            Language = GetString(document, nameof(SidecarRecord.Language)),
            Confidence = document.TryGetValue(nameof(SidecarRecord.Confidence), out var confidence) ? confidence.AsDouble() : 0,
            ReasoningSummary = GetString(document, nameof(SidecarRecord.ReasoningSummary)),
            DetectedText = document.TryGetValue(nameof(SidecarRecord.DetectedText), out var detectedText)
                ? detectedText.AsListOfString().ToArray()
                : null,
            ScannedAtUtc = document.TryGetValue(nameof(SidecarRecord.ScannedAtUtc), out var scannedAt) && DateTimeOffset.TryParse(scannedAt.AsString(), out var parsed)
                ? parsed
                : null,
            ErrorMessage = GetString(document, nameof(SidecarRecord.ErrorMessage)),
            SourceFileName = GetString(document, nameof(SidecarRecord.SourceFileName)),
            AiModel = GetString(document, nameof(SidecarRecord.AiModel)),
            RawModelResponse = GetString(document, nameof(SidecarRecord.RawModelResponse))
        };
    }

    private static string? GetString(Document document, string key)
    {
        return document.TryGetValue(key, out var value) ? value.AsString() : null;
    }
}

/// <summary>
/// Lenient, fully-optional representation of a sidecar record, used when reading/merging
/// arbitrary sidecar contents (as opposed to <see cref="CardAnalysisResult"/> which is only
/// produced by AI Analysis).
/// </summary>
internal sealed record SidecarRecord
{
    public string? AnalysisStatus { get; init; }
    public string? ReviewStatus { get; init; }
    public string? CardName { get; init; }
    public string? CardNumber { get; init; }
    public string? SetName { get; init; }
    public string? Rarity { get; init; }
    public string? Language { get; init; }
    public double Confidence { get; init; }
    public string? ReasoningSummary { get; init; }
    public string[]? DetectedText { get; init; }
    public DateTimeOffset? ScannedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
    public string? SourceFileName { get; init; }
    public string? AiModel { get; init; }
    public string? RawModelResponse { get; init; }
}
