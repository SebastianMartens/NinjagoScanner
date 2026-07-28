using NinjagoScanner.Web.Components;
using Microsoft.Extensions.FileProviders;
using NinjagoScanner.Scanner;
using NinjagoScanner.Scanner.Abstractions;
using NinjagoScanner.Web.Services;

var builder = WebApplication.CreateBuilder(args);
var cardPhotosDirectory = ResolveCardPhotosDirectory(builder.Configuration, builder.Environment.ContentRootPath);
var maxUploadBytes = ResolveMaxUploadBytes(builder.Configuration);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton(new CardCatalogService(cardPhotosDirectory, maxUploadBytes));
builder.Services.AddSingleton<IGeminiCardScanner, GeminiCardScanner>();

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
    var roots = new[]
    {
        contentRootPath,
        AppContext.BaseDirectory,
        Environment.CurrentDirectory
    };

    return roots
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);
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
