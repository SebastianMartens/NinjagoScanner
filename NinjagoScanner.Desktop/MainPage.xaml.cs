using System.Diagnostics;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NinjagoScanner_Desktop;

public sealed partial class MainPage : Page
{
    private static readonly Uri DefaultWebApplicationUri = new("http://127.0.0.1:5000/");

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

        var startupResult = await EnsureWebApplicationAvailableAsync();
        if (!startupResult.IsSuccess)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            BrowserView.NavigateToString(BuildErrorHtml(startupResult.Message, startupResult.Uri));
            return;
        }

        UpdateStatus("Lade Web-Anwendung...");
        BrowserView.NavigationCompleted += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;
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
        var targetUri = GetWebApplicationUri();

        if (await IsWebAppReachableAsync(targetUri))
        {
            return StartupResult.Success(targetUri);
        }

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
        return StartupResult.Failure(targetUri, $"Keine startbare Web-Anwendung gefunden.<br/><br/>Gepruefte Pfade:<br/>{candidates}<br/><br/>Publishe zuerst das Webprojekt oder setze NINJAGO_WEB_EXE bzw. NINJAGO_WEB_URL.");
    }

    private static Uri GetWebApplicationUri()
    {
        var configuredUrl = Environment.GetEnvironmentVariable("NINJAGO_WEB_URL");
        return string.IsNullOrWhiteSpace(configuredUrl)
            ? DefaultWebApplicationUri
            : new Uri(configuredUrl, UriKind.Absolute);
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

            process = Process.Start(startInfo);
            executablePath = candidate;
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
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NinjagoScanner.Web", "bin", "Release", "net10.0", "publish", "NinjagoScanner.Web.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NinjagoScanner.Web", "bin", "Debug", "net10.0", "NinjagoScanner.Web.exe"))
        };
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
        StatusTextBlock.Text = message;
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
