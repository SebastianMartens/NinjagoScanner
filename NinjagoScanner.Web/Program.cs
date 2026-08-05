using NinjagoScanner.Web.Components;
using Microsoft.Extensions.FileProviders;
using NinjagoScanner.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var cardPhotosDirectory = ResolveCardPhotosDirectory(builder.Configuration, builder.Environment.ContentRootPath);
var catalogServiceAddress = ResolveCatalogServiceAddress(builder.Configuration);
var pictureServiceAddress = ResolvePictureServiceAddress(builder.Configuration);
var maxUploadBytes = ResolveMaxUploadBytes(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton(new CardCatalogService(cardPhotosDirectory, maxUploadBytes, catalogServiceAddress, pictureServiceAddress));
builder.Services.AddSingleton(new PictureServiceClient(pictureServiceAddress));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

if (Directory.Exists(cardPhotosDirectory))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(cardPhotosDirectory),
        RequestPath = "/cardFotos"
    });
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string ResolveCardPhotosDirectory(IConfiguration configuration, string contentRootPath)
{
    var configuredDirectory = configuration["CardPhotos:Directory"]
                              ?? configuration["CardPhotosDirectory"]
                              ?? Environment.GetEnvironmentVariable("NINJAGO_CARD_PHOTOS_DIR")
                              ?? Environment.GetEnvironmentVariable("CARD_PHOTOS_DIRECTORY");

    if (!string.IsNullOrWhiteSpace(configuredDirectory))
    {
        return GetAbsolutePath(configuredDirectory, contentRootPath);
    }

    foreach (var baseDirectory in GetSearchRoots(contentRootPath))
    {
        var discovered = TryFindSharedCardPhotosDirectory(baseDirectory);
        if (discovered is not null)
        {
            return discovered;
        }
    }

    return Path.GetFullPath(Path.Combine(contentRootPath, "..", "cardFotos"));
}

static string GetAbsolutePath(string directory, string contentRootPath)
{
    return Path.IsPathRooted(directory)
        ? Path.GetFullPath(directory)
        : Path.GetFullPath(Path.Combine(contentRootPath, directory));
}

static IEnumerable<string> GetSearchRoots(string contentRootPath)
{
    var roots = new List<string>
    {
        contentRootPath,
        AppContext.BaseDirectory,
        Environment.CurrentDirectory
    };

    var gitMainRepoRoot = TryGetGitMainRepoRoot(contentRootPath);
    if (gitMainRepoRoot is not null)
    {
        roots.Add(gitMainRepoRoot);
    }

    return roots
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
}

static string? TryGetGitMainRepoRoot(string startDirectory)
{
    var dir = new DirectoryInfo(startDirectory);
    while (dir is not null)
    {
        var gitFile = Path.Combine(dir.FullName, ".git");
        if (File.Exists(gitFile))
        {
            var content = File.ReadAllText(gitFile).Trim();
            if (content.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
            {
                var gitdirPath = content["gitdir:".Length..].Trim();
                // Path format: <main_repo>/.git/worktrees/<branch>
                var worktreesDir = new DirectoryInfo(gitdirPath)?.Parent?.Parent;
                if (worktreesDir?.Parent is { } mainRepoRoot)
                {
                    return mainRepoRoot.FullName;
                }
            }
        }
        dir = dir.Parent;
    }
    return null;
}

static string? TryFindSharedCardPhotosDirectory(string startDirectory)
{
    var directoryInfo = new DirectoryInfo(startDirectory);

    while (directoryInfo is not null)
    {
        var candidate = Path.Combine(directoryInfo.FullName, "cardFotos");
        if (Directory.Exists(candidate) && !IsInsideBinDirectory(candidate))
        {
            return candidate;
        }

        directoryInfo = directoryInfo.Parent;
    }

    return null;
}

static bool IsInsideBinDirectory(string path)
{
    var fullPath = Path.GetFullPath(path);
    var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
}

static long ResolveMaxUploadBytes(IConfiguration configuration)
{
    const long defaultMaxUploadBytes = 15 * 1024 * 1024;

    var configuredValue = configuration["CardPhotos:MaxUploadBytes"]
                          ?? configuration["CardPhotosMaxUploadBytes"]
                          ?? Environment.GetEnvironmentVariable("CARD_PHOTOS_MAX_UPLOAD_BYTES");

    if (long.TryParse(configuredValue, out var parsedValue) && parsedValue > 0)
    {
        return parsedValue;
    }

    return defaultMaxUploadBytes;
}

static string ResolveCatalogServiceAddress(IConfiguration configuration)
{
    return configuration["CatalogService:Address"]
           ?? Environment.GetEnvironmentVariable("CATALOG_SERVICE_ADDRESS")
           ?? "http://localhost:5073";
}

static string ResolvePictureServiceAddress(IConfiguration configuration)
{
    return configuration["PictureService:Address"]
           ?? Environment.GetEnvironmentVariable("PICTURE_SERVICE_ADDRESS")
           ?? "http://localhost:5169";
}
