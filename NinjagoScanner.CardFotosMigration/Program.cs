using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using NinjagoScanner.CardFotosMigration;
using NinjagoScanner.PictureService;

// One-time, copy-only migration: reads local cardFotos/ (image + optional ".json" sidecar per
// photo) and writes each photo into S3 (bytes) + DynamoDB (sidecar record), under a freshly
// generated photo ID. Never deletes or modifies anything under cardFotos/. Safe to re-run: a
// manifest file (outside cardFotos/) tracks what's already been migrated and skips it.
//
// Requires real AWS credentials (via the standard AWS SDK credential chain — env vars, shared
// credentials file, or an assumed role) and the target S3 bucket / DynamoDB table to already
// exist (see infra/modules/photo-storage and infra/modules/sidecar-table). This tool cannot be
// exercised end-to-end without those, since no AWS account is reachable from a plain dev machine.

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var sourceDirectory = configuration["source"] ?? ResolveDefaultCardFotosDirectory();
var bucketName = configuration["bucket"] ?? configuration["PHOTOS_BUCKET_NAME"]
    ?? Fail("Missing --bucket (or PHOTOS_BUCKET_NAME) — the target S3 bucket for photo bytes.");
var tableName = configuration["table"] ?? configuration["SIDECAR_TABLE_NAME"]
    ?? Fail("Missing --table (or SIDECAR_TABLE_NAME) — the target DynamoDB table for sidecar records.");
var manifestPath = configuration["manifest"]
    ?? Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDirectory))!, ".card-fotos-migration-manifest.json");
var maxDegreeOfParallelism = int.TryParse(configuration["parallelism"], out var parsedParallelism) ? Math.Max(1, parsedParallelism) : 4;
var dryRun = bool.TryParse(configuration["dry-run"], out var parsedDryRun) && parsedDryRun;

if (!Directory.Exists(sourceDirectory))
{
    Console.Error.WriteLine($"Quellordner '{sourceDirectory}' wurde nicht gefunden.");
    return 1;
}

Console.WriteLine($"Quelle:    {sourceDirectory}");
Console.WriteLine($"S3 Bucket: {bucketName}");
Console.WriteLine($"DynamoDB:  {tableName}");
Console.WriteLine($"Manifest:  {manifestPath}");
Console.WriteLine(dryRun ? "Modus:     DRY RUN (kein Upload, keine DB-Schreibvorgänge)" : "Modus:     LIVE");
Console.WriteLine();

var manifest = Manifest.Load(manifestPath);

using var s3Client = dryRun ? null : new AmazonS3Client();
using var dynamoDbClient = dryRun ? null : new AmazonDynamoDBClient();
var sidecarTable = dynamoDbClient is null ? null : new SidecarTable(dynamoDbClient, tableName);

var imageFiles = Directory
    .EnumerateFiles(sourceDirectory)
    .Where(path => ScannerConfig.SupportedExtensions.Contains(Path.GetExtension(path)))
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

Console.WriteLine($"{imageFiles.Count} Kartenfotos gefunden.");

var totalFiles = imageFiles.Count;
var migratedCount = 0;
var alreadyMigratedCount = 0;
var errorCount = 0;
var progressLock = new Lock();
var processed = 0;

await Parallel.ForEachAsync(
    imageFiles,
    new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
    async (imagePath, cancellationToken) =>
    {
        var imageFileName = Path.GetFileName(imagePath);

        try
        {
            if (manifest.TryGet(imageFileName, out var existing))
            {
                Interlocked.Increment(ref alreadyMigratedCount);
                ReportProgress(imageFileName, $"übersprungen (bereits migriert als {existing.PhotoId})");
                return;
            }

            var photoId = Guid.NewGuid().ToString("N");
            var sidecarPath = imagePath + ".json";
            var record = File.Exists(sidecarPath)
                ? ReadLegacySidecar(sidecarPath, imageFileName)
                : null;
            record ??= new SidecarRecord { AnalysisStatus = "pending", SourceFileName = imageFileName };
            if (string.IsNullOrWhiteSpace(record.SourceFileName))
            {
                record = record with { SourceFileName = imageFileName };
            }

            if (!dryRun)
            {
                await s3Client!.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = PhotoStore.BuildObjectKey(photoId),
                    FilePath = imagePath
                }, cancellationToken);

                await sidecarTable!.PutAsync(photoId, record, cancellationToken);
            }

            if (!dryRun)
            {
                manifest.RecordAndMaybeFlush(imageFileName, photoId);
            }

            Interlocked.Increment(ref migratedCount);
            ReportProgress(imageFileName, dryRun ? "würde migriert werden" : $"migriert -> {photoId}");
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref errorCount);
            ReportProgress(imageFileName, $"FEHLER: {exception.Message}");
        }
    });

if (!dryRun)
{
    manifest.Flush();
}

Console.WriteLine();
Console.WriteLine("Zusammenfassung:");
Console.WriteLine($"  Gesamt:            {totalFiles}");
Console.WriteLine($"  Migriert:          {migratedCount}");
Console.WriteLine($"  Bereits migriert:  {alreadyMigratedCount}");
Console.WriteLine($"  Fehler:            {errorCount}");
Console.WriteLine();
Console.WriteLine($"cardFotos/ wurde ausschliesslich gelesen, niemals verändert oder gelöscht.");

return errorCount == 0 ? 0 : 2;

void ReportProgress(string imageFileName, string outcome)
{
    lock (progressLock)
    {
        processed++;
        Console.WriteLine($"[{processed}/{totalFiles}] {imageFileName}: {outcome}");
    }
}

static SidecarRecord? ReadLegacySidecar(string sidecarPath, string imageFileName)
{
    var json = File.ReadAllText(sidecarPath);
    var record = JsonSerializer.Deserialize<SidecarRecord>(json, ScannerJsonOptions.Default);
    if (record is not null && !string.IsNullOrWhiteSpace(record.AnalysisStatus))
    {
        return record;
    }

    // Sidecars written before "status" was renamed to "AnalysisStatus" still carry the old key.
    using var document = JsonDocument.Parse(json);
    foreach (var property in document.RootElement.EnumerateObject())
    {
        if (string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind == JsonValueKind.String)
        {
            return (record ?? new SidecarRecord { SourceFileName = imageFileName }) with { AnalysisStatus = property.Value.GetString() };
        }
    }

    return record;
}

static string ResolveDefaultCardFotosDirectory()
{
    var candidates = new[]
    {
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "cardFotos")),
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardFotos")),
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "cardFotos")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "cardFotos"))
    };

    return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
}

static string Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
    return string.Empty;
}
