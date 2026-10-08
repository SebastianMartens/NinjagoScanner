namespace NinjagoScanner.Web.Seeding;

/// <summary>Command-line `seed` mode: `dotnet run -- seed` seeds the configured database and exits.</summary>
public static class SeedMode
{
    private const string DefaultDatabasePath = "Data/users.db";

    public static bool IsRequested(string[] args) =>
        args.Contains("seed", StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The default path is the developer's real user database (git-ignored but in the repo
    /// folder); seeding must never write fixed users into it.
    /// </summary>
    public static void EnsureNotDefaultDatabase(string databasePath)
    {
        var normalized = Path.GetFullPath(databasePath);
        var defaultPath = Path.GetFullPath(DefaultDatabasePath);
        if (string.Equals(normalized, defaultPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing to seed the default database '{DefaultDatabasePath}'. " +
                "Set AUTH_DATABASE_PATH to a separate file (dev.ps1 does this).");
        }
    }

    /// <summary>SEED_MANIFEST_PATH, else testdata/manifest.json searched upwards from the working directory.</summary>
    public static string ResolveManifestPath(IConfiguration configuration)
    {
        var configured = configuration["Seed:ManifestPath"] ?? Environment.GetEnvironmentVariable("SEED_MANIFEST_PATH");
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "testdata", "manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("testdata/manifest.json not found; set SEED_MANIFEST_PATH.");
    }
}
