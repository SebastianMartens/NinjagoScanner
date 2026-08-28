using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>Builds the post-scan status message shown on the Overview page after a manual Gemini scan.</summary>
internal static class ScanStatusMessageFormatter
{
    public static string BuildMessage(ScanSummaryDto summary)
    {
        if (summary.HasConfigurationError)
        {
            return summary.Message ?? "Scan konnte nicht gestartet werden.";
        }

        var countsMessage = $"{summary.Processed} verarbeitet, {summary.Skipped} uebersprungen, {summary.Uncertain} unsicher, {summary.Failed} fehlgeschlagen.";

        return summary.StoppedEarly
            ? $"Scan vorzeitig abgebrochen (Gemini wiederholt nicht erreichbar): {countsMessage} Spaeter erneut versuchen."
            : $"Scan fertig: {countsMessage}";
    }
}
