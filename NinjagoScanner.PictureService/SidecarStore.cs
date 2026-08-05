using System.Text;
using System.Text.Json;

namespace NinjagoScanner.PictureService;

internal static class SidecarStore
{
    public static string GetSidecarPath(string imagePath)
    {
        return imagePath + ".json";
    }

    public static async Task WriteAsync(string sidecarPath, CardAnalysisResult result, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(result, ScannerJsonOptions.Pretty);
        await File.WriteAllTextAsync(sidecarPath, json, Encoding.UTF8, cancellationToken);
    }
}
