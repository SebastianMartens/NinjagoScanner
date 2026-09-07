using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NinjagoScanner.PictureService;
using NinjagoScanner.Web.Data;

// One-time migration: assigns every pre-existing (collection-less) photo and sidecar to a single
// Collection owned by an existing user (see openspec/changes/add-collection-data-isolation). Run
// once, after deploying the code that introduces the Collection concept, before decommissioning
// the old-schema DynamoDB table and the un-prefixed S3 objects. Copy-only: never deletes or
// modifies anything in the old bucket/table, and safe to re-run (re-copies the same content to the
// same destination keys).
//
// Old DynamoDB schema: hash key "PhotoId" only. New schema (see SidecarTable): hash key
// "CollectionId", range key "PhotoId" - a different table, not an in-place alter. SidecarTable's
// DocumentModel Table is bound to the new schema and can't address the old one, so this reads the
// old table with the low-level DynamoDB API directly and writes through SidecarTable into the new
// table. Requires real AWS credentials (standard AWS SDK credential chain) and read/write access
// to the Web app's SQLite users.db.

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var oldBucketName = configuration["old-bucket"] ?? configuration["PHOTOS_BUCKET_NAME"]
    ?? Fail("Missing --old-bucket (or PHOTOS_BUCKET_NAME) - the S3 bucket holding pre-collection photos.");
var newBucketName = configuration["new-bucket"] ?? oldBucketName;
var oldTableName = configuration["old-table"] ?? configuration["SIDECAR_TABLE_NAME"]
    ?? Fail("Missing --old-table (or SIDECAR_TABLE_NAME) - the DynamoDB table using the old PhotoId-only key schema.");
var newTableName = configuration["new-table"]
    ?? Fail("Missing --new-table - the DynamoDB table using the new CollectionId+PhotoId key schema.");
var username = configuration["username"]
    ?? Fail("Missing --username - the existing account that should own the migrated data (e.g. 'anton').");
var authDatabasePath = configuration["auth-db"]
    ?? Fail("Missing --auth-db - path to the Web app's SQLite users.db.");
var dryRun = bool.TryParse(configuration["dry-run"], out var parsedDryRun) && parsedDryRun;

Console.WriteLine($"Username:   {username}");
Console.WriteLine($"Auth DB:    {authDatabasePath}");
Console.WriteLine($"Old bucket: {oldBucketName}  (prefix photos/<photoId>)");
Console.WriteLine($"New bucket: {newBucketName}  (prefix photos/<collectionId>/<photoId>)");
Console.WriteLine($"Old table:  {oldTableName}  (key: PhotoId)");
Console.WriteLine($"New table:  {newTableName}  (key: CollectionId + PhotoId)");
Console.WriteLine(dryRun ? "Modus:      DRY RUN (keine Schreibvorgänge)" : "Modus:      LIVE");
Console.WriteLine();

// Step 1: resolve (or create) the target collection, owned by the given user.
var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={authDatabasePath}").Options;
await using var dbContext = new AppDbContext(dbOptions);

var normalizedUsername = username.ToUpperInvariant();
var user = await dbContext.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUsername);
if (user is null)
{
    Console.Error.WriteLine($"Benutzer '{username}' wurde in '{authDatabasePath}' nicht gefunden.");
    return 1;
}

var existingCollection = await dbContext.CollectionMemberships
    .Where(m => m.UserId == user.Id && m.Role == CollectionRole.Owner)
    .Join(dbContext.Collections, m => m.CollectionId, c => c.Id, (m, c) => c)
    .FirstOrDefaultAsync();

Collection collection;
if (existingCollection is not null)
{
    collection = existingCollection;
    Console.WriteLine($"Benutzer '{username}' besitzt bereits die Sammlung '{collection.Id}' ({collection.Name}) - wird wiederverwendet.");
}
else
{
    collection = new Collection { Name = $"Sammlung von {user.UserName}" };
    Console.WriteLine($"Neue Sammlung '{collection.Id}' fuer '{username}' wird erstellt.");
    if (!dryRun)
    {
        dbContext.Collections.Add(collection);
        dbContext.CollectionMemberships.Add(new CollectionMembership
        {
            CollectionId = collection.Id,
            UserId = user.Id,
            Role = CollectionRole.Owner
        });
        await dbContext.SaveChangesAsync();
    }
}

var collectionId = collection.Id;
Console.WriteLine($"Ziel-Sammlung: {collectionId}");
Console.WriteLine();

// Step 2: copy every sidecar from the old table (PhotoId-only key) into the new table
// (CollectionId+PhotoId key) under collectionId, reading the old schema with the low-level API.
using var dynamoDbClient = new AmazonDynamoDBClient();
var newSidecarTable = new SidecarTable(dynamoDbClient, newTableName);

var migratedSidecars = 0;
var scanRequest = new ScanRequest { TableName = oldTableName };
ScanResponse scanResponse;
do
{
    scanResponse = await dynamoDbClient.ScanAsync(scanRequest);
    foreach (var item in scanResponse.Items)
    {
        if (!item.TryGetValue("PhotoId", out var photoIdAttribute) || string.IsNullOrEmpty(photoIdAttribute.S))
        {
            continue;
        }

        var photoId = photoIdAttribute.S;
        var record = ToSidecarRecord(item);

        Console.WriteLine($"  Sidecar {photoId}: {(dryRun ? "würde migriert werden" : "migriert")}");
        if (!dryRun)
        {
            await newSidecarTable.PutAsync(collectionId, photoId, record, CancellationToken.None);
        }

        migratedSidecars++;
    }

    scanRequest.ExclusiveStartKey = scanResponse.LastEvaluatedKey is { Count: > 0 } ? scanResponse.LastEvaluatedKey : null;
} while (scanRequest.ExclusiveStartKey is not null);

Console.WriteLine($"Sidecars migriert: {migratedSidecars}");
Console.WriteLine();

// Step 3: copy every photo from photos/<photoId> to photos/<collectionId>/<photoId> in S3.
// Keys already containing a second "/" are skipped - they're already collection-prefixed (a
// previous partial run, or the new scheme already in use), not part of the old scheme.
using var s3Client = new AmazonS3Client();

var migratedPhotos = 0;
const string keyPrefix = "photos/";
var listRequest = new ListObjectsV2Request { BucketName = oldBucketName, Prefix = keyPrefix };
ListObjectsV2Response listResponse;
do
{
    listResponse = await s3Client.ListObjectsV2Async(listRequest);
    foreach (var s3Object in listResponse.S3Objects)
    {
        var relativeKey = s3Object.Key[keyPrefix.Length..];
        if (relativeKey.Contains('/'))
        {
            continue;
        }

        var photoId = relativeKey;
        var newKey = $"{keyPrefix}{collectionId}/{photoId}";

        Console.WriteLine($"  Foto {photoId}: {(dryRun ? "würde kopiert werden" : "kopiert")} -> {newKey}");
        if (!dryRun)
        {
            await s3Client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = oldBucketName,
                SourceKey = s3Object.Key,
                DestinationBucket = newBucketName,
                DestinationKey = newKey
            });
        }

        migratedPhotos++;
    }

    listRequest.ContinuationToken = listResponse.NextContinuationToken;
} while (listResponse.IsTruncated == true);

Console.WriteLine($"Fotos migriert: {migratedPhotos}");
Console.WriteLine();
Console.WriteLine("Die alte Tabelle und die alten S3-Objekte wurden ausschliesslich gelesen, niemals verändert oder gelöscht.");

return 0;

static SidecarRecord ToSidecarRecord(Dictionary<string, AttributeValue> item)
{
    return new SidecarRecord
    {
        AnalysisStatus = GetString(item, "AnalysisStatus"),
        ReviewStatus = GetString(item, "ReviewStatus"),
        CardName = GetString(item, "CardName"),
        CardNumber = GetString(item, "CardNumber"),
        SetName = GetString(item, "SetName"),
        Rarity = GetString(item, "Rarity"),
        Language = GetString(item, "Language"),
        Confidence = item.TryGetValue("Confidence", out var confidence) && confidence.N is not null
            ? double.Parse(confidence.N, CultureInfo.InvariantCulture)
            : 0,
        ReasoningSummary = GetString(item, "ReasoningSummary"),
        DetectedText = item.TryGetValue("DetectedText", out var detectedText) && detectedText.L is { Count: > 0 } list
            ? list.Where(value => value.S is not null).Select(value => value.S).ToArray()
            : null,
        ScannedAtUtc = GetString(item, "ScannedAtUtc") is { } scannedAt && DateTimeOffset.TryParse(scannedAt, out var parsed)
            ? parsed
            : null,
        ErrorMessage = GetString(item, "ErrorMessage"),
        SourceFileName = GetString(item, "SourceFileName"),
        AiModel = GetString(item, "AiModel"),
        RawModelResponse = GetString(item, "RawModelResponse")
    };
}

static string? GetString(Dictionary<string, AttributeValue> item, string key)
{
    return item.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value.S) ? value.S : null;
}

static string Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
    return string.Empty;
}
