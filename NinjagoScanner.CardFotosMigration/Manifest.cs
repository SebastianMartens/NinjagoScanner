using System.Text.Json;

namespace NinjagoScanner.CardFotosMigration;

/// <summary>
/// Tracks which local files under cardFotos/ have already been copied into S3 + DynamoDB, keyed
/// by the image's file name, so an interrupted run can resume without re-uploading everything.
/// Never touches anything under cardFotos/ itself — lives at whatever <c>--manifest</c> path is
/// configured (default: a sibling of cardFotos/, not inside it).
/// </summary>
internal sealed class Manifest
{
    private readonly string path;
    private readonly Dictionary<string, ManifestEntry> entries;
    private readonly Lock writeLock = new();
    private int pendingUnflushedWrites;

    private Manifest(string path, Dictionary<string, ManifestEntry> entries)
    {
        this.path = path;
        this.entries = entries;
    }

    public static Manifest Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Manifest(path, new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase));
        }

        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(json)
                      ?? new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        return new Manifest(path, new Dictionary<string, ManifestEntry>(entries, StringComparer.OrdinalIgnoreCase));
    }

    public bool TryGet(string imageFileName, out ManifestEntry entry)
    {
        lock (writeLock)
        {
            return entries.TryGetValue(imageFileName, out entry!);
        }
    }

    /// <summary>Records a successful migration and flushes to disk every few writes, so a crash loses at most a handful of already-uploaded files' bookkeeping (they'd just be re-uploaded, harmlessly, on the next run).</summary>
    public void RecordAndMaybeFlush(string imageFileName, string photoId)
    {
        lock (writeLock)
        {
            entries[imageFileName] = new ManifestEntry(photoId, DateTimeOffset.UtcNow);
            pendingUnflushedWrites++;
            if (pendingUnflushedWrites >= 25)
            {
                FlushLocked();
            }
        }
    }

    public void Flush()
    {
        lock (writeLock)
        {
            FlushLocked();
        }
    }

    private void FlushLocked()
    {
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        pendingUnflushedWrites = 0;
    }
}

internal readonly record struct ManifestEntry(string PhotoId, DateTimeOffset MigratedAtUtc);
