using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace NinjagoScanner_Desktop;

public sealed partial class MainPage : Page
{
    private static readonly Uri ManagedDesktopWebUri = new("http://127.0.0.1:5000/");
    private static readonly Uri LaunchSettingsFallbackUri = new("http://localhost:5273/");

    private Process? _webProcess;
    private bool _startedWebProcess;
    private bool _isInitialized;

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        UpdateStatus("Pruefe Web-Anwendung...");

        try
        {
            await BrowserView.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            ShowStartupError($"WebView2 konnte nicht initialisiert werden: {ex.Message}", ManagedDesktopWebUri);
            return;
        }

        var startupResult = await EnsureWebApplicationAvailableAsync();
        if (!startupResult.IsSuccess)
        {
            ShowStartupError(startupResult.Message, startupResult.Uri);
            return;
        }

        UpdateStatus("Lade Web-Anwendung...");
        BrowserView.NavigationCompleted += OnBrowserNavigationCompleted;
        BrowserView.Source = startupResult.Uri;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (_startedWebProcess && _webProcess is { HasExited: false })
        {
            try
            {
                _webProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore shutdown errors when closing the desktop host.
            }
        }
    }

    private async Task<StartupResult> EnsureWebApplicationAvailableAsync()
    {
        var targetUris = GetWebApplicationUris();

        foreach (var uri in targetUris)
        {
            if (await IsWebAppReachableAsync(uri))
            {
                return StartupResult.Success(uri);
            }
        }

        var targetUri = GetManagedStartupUri(targetUris);

        UpdateStatus("Starte Web-App...");

        if (TryStartWebApplication(targetUri, out var process, out var executablePath))
        {
            _webProcess = process;
            _startedWebProcess = true;

            if (await WaitForWebAppAsync(targetUri))
            {
                return StartupResult.Success(targetUri);
            }

            return StartupResult.Failure(targetUri, $"Die Web-Anwendung wurde aus '{executablePath}' gestartet, war aber unter {targetUri} nicht erreichbar.");
        }

        var candidates = string.Join("<br/>", GetWebExecutableCandidates().Select(System.Net.WebUtility.HtmlEncode));
        return StartupResult.Failure(targetUri, $"Keine startbare Web-Anwendung gefunden.<br/><br/>Gepruefte EXE-Pfade:<br/>{candidates}<br/><br/>Setze NINJAGO_WEB_URL (z. B. http://localhost:5273) oder NINJAGO_WEB_EXE.");
    }

    private static IReadOnlyList<Uri> GetWebApplicationUris()
    {
        var configuredUrl = Environment.GetEnvironmentVariable("NINJAGO_WEB_URL");

        if (!string.IsNullOrWhiteSpace(configuredUrl) && Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configuredUri))
        {
            return [NormalizeUri(configuredUri)];
        }

        var uris = new List<Uri>();
        uris.AddRange(GetLaunchSettingsUris());
        uris.Add(LaunchSettingsFallbackUri);
        uris.Add(ManagedDesktopWebUri);

        return uris
            .Select(NormalizeUri)
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Uri GetManagedStartupUri(IEnumerable<Uri> discoveredUris)
    {
        var configuredUrl = Environment.GetEnvironmentVariable("NINJAGO_WEB_URL");
        if (!string.IsNullOrWhiteSpace(configuredUrl) && Uri.TryCreate(configuredUrl, UriKind.Absolute, out var configuredUri))
        {
            return NormalizeUri(configuredUri);
        }

        var managedCandidate = discoveredUris.FirstOrDefault(uri =>
            uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && uri.Port == ManagedDesktopWebUri.Port);

        return managedCandidate ?? ManagedDesktopWebUri;
    }

    private static IReadOnlyList<Uri> GetLaunchSettingsUris()
    {
        var discoveredUris = new List<Uri>();

        foreach (var launchSettingsPath in GetLaunchSettingsCandidates())
        {
            if (!File.Exists(launchSettingsPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(launchSettingsPath);
                using var document = JsonDocument.Parse(stream);

                if (!document.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var profile in profiles.EnumerateObject())
                {
                    if (!profile.Value.TryGetProperty("applicationUrl", out var applicationUrlProperty)
                        || applicationUrlProperty.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var urls = applicationUrlProperty.GetString();
                    if (string.IsNullOrWhiteSpace(urls))
                    {
                        continue;
                    }

                    foreach (var rawUrl in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                        {
                            continue;
                        }

                        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        discoveredUris.Add(uri);
                    }
                }
            }
            catch
            {
                // Ignore malformed launchSettings.json and continue with known defaults.
            }
        }

        return discoveredUris;
    }

    private static IEnumerable<string> GetLaunchSettingsCandidates()
    {
        return new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NinjagoScanner.Web", "launchSettings.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NinjagoScanner.Web", "Properties", "launchSettings.json")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "NinjagoScanner.Web", "Properties", "launchSettings.json"))
        };
    }

    private static Uri NormalizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }

    private static bool TryStartWebApplication(Uri targetUri, out Process? process, out string executablePath)
    {
        process = null;
        executablePath = string.Empty;

        var configuredPath = Environment.GetEnvironmentVariable("NINJAGO_WEB_EXE");
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(Path.GetFullPath(configuredPath));
        }

        candidates.AddRange(GetWebExecutableCandidates());

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = candidate,
                WorkingDirectory = Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.Environment["ASPNETCORE_URLS"] = targetUri.ToString();
            startInfo.Environment["NINJAGO_CARD_PHOTOS_DIR"] = ResolveCardPhotosDirectory();

            process = Process.Start(startInfo);
            executablePath = candidate;
            return process is not null;
        }

        if (TryStartWebApplicationViaDotnetRun(targetUri, out process, out executablePath))
        {
            return true;
        }

        return false;
    }

    private static bool TryStartWebApplicationViaDotnetRun(Uri targetUri, out Process? process, out string executablePath)
    {
        process = null;
        executablePath = string.Empty;

        foreach (var projectFile in GetWebProjectCandidates())
        {
            if (!File.Exists(projectFile))
            {
                continue;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectFile}\" --no-launch-profile --urls \"{targetUri}\"",
                WorkingDirectory = Path.GetDirectoryName(projectFile) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.Environment["NINJAGO_CARD_PHOTOS_DIR"] = ResolveCardPhotosDirectory();

            process = Process.Start(startInfo);
            executablePath = $"dotnet run --project {projectFile}";
            return process is not null;
        }

        return false;
    }

    private static IReadOnlyList<string> GetWebExecutableCandidates()
    {
        return new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NinjagoScanner.Web.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NinjagoScanner.Web", "NinjagoScanner.Web.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "NinjagoScanner.Web", "bin", "Release", "net10.0", "publish", "NinjagoScanner.Web.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "NinjagoScanner.Web", "bin", "Debug", "net10.0", "publish", "NinjagoScanner.Web.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "NinjagoScanner.Web", "bin", "Debug", "net10.0", "NinjagoScanner.Web.exe"))
        };
    }

    private static IReadOnlyList<string> GetWebProjectCandidates()
    {
        return new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "NinjagoScanner.Web", "NinjagoScanner.Web.csproj")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NinjagoScanner.Web", "NinjagoScanner.Web.csproj"))
        };
    }

    private static string ResolveCardPhotosDirectory()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("NINJAGO_CARD_PHOTOS_DIR");
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.GetFullPath(configuredDirectory);
        }

        foreach (var searchRoot in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directoryInfo = new DirectoryInfo(searchRoot);

            while (directoryInfo is not null)
            {
                var candidate = Path.Combine(directoryInfo.FullName, "cardFotos");
                if (Directory.Exists(candidate) && !IsInsideBinDirectory(candidate))
                {
                    return candidate;
                }

                directoryInfo = directoryInfo.Parent;
            }
        }

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "cardFotos"));
    }

    private static bool IsInsideBinDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var segments = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> IsWebAppReachableAsync(Uri targetUri)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        try
        {
            using var response = await httpClient.GetAsync(targetUri);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForWebAppAsync(Uri targetUri)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await IsWebAppReachableAsync(targetUri))
            {
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private void UpdateStatus(string message)
    {
        LoadingProgressRing.IsActive = true;
        StatusTextBlock.Text = message;
    }

    private void ShowStartupError(string message, Uri targetUri)
    {
        LoadingProgressRing.IsActive = false;
        StatusTextBlock.Text = $"Startfehler: {message}\n\nErwartete URL: {targetUri}";
        LoadingOverlay.Visibility = Visibility.Visible;

        try
        {
            BrowserView.NavigateToString(BuildErrorHtml(message, targetUri));
        }
        catch
        {
            // If WebView2 itself is unavailable, the overlay text remains visible.
        }
    }

    private void OnBrowserNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ShowStartupError($"Navigation fehlgeschlagen ({args.WebErrorStatus}).", sender.Source ?? ManagedDesktopWebUri);
    }


    private static string BuildErrorHtml(string message, Uri targetUri)
    {
        return $$"""
<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="utf-8" />
  <title>Ninjago Scanner Desktop</title>
  <style>
    body { font-family: Segoe UI, sans-serif; background: #101418; color: #f6f1e8; margin: 0; display: grid; min-height: 100vh; place-items: center; }
    .panel { max-width: 760px; padding: 32px; border-radius: 20px; background: #1a232b; box-shadow: 0 20px 40px rgba(0,0,0,.25); }
    h1 { margin-top: 0; font-size: 30px; }
    p { line-height: 1.55; color: #d3d9df; }
    code { background: #0e1419; padding: 2px 6px; border-radius: 6px; }
  </style>
</head>
<body>
  <div class="panel">
    <h1>Web-Anwendung konnte nicht geladen werden</h1>
    <p>{{message}}</p>
    <p>Erwartete URL: <code>{{targetUri}}</code></p>
  </div>
</body>
</html>
""";
    }

    private sealed record StartupResult(bool IsSuccess, Uri Uri, string Message)
    {
        public static StartupResult Success(Uri uri) => new(true, uri, string.Empty);

        public static StartupResult Failure(Uri uri, string message) => new(false, uri, message);
    }
}
