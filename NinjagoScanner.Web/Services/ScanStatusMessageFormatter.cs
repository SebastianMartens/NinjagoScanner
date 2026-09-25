using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>Builds the post-analysis status message shown on the Upload page after a manual batch analysis.</summary>
internal static class ScanStatusMessageFormatter
{
    public static string BuildMessage(ScanSummaryDto summary)
    {
        if (summary.HasConfigurationError)
        {
            return summary.Message ?? "Analyse konnte nicht gestartet werden.";
        }

        var countsMessage = $"{summary.Processed} verarbeitet, {summary.Skipped} uebersprungen, {summary.Uncertain} unsicher, {summary.Failed} fehlgeschlagen.";

        return summary.StoppedEarly
            ? $"Analyse vorzeitig abgebrochen (Analysedienst wiederholt nicht erreichbar): {countsMessage} Spaeter erneut versuchen."
            : $"Analyse fertig: {countsMessage}";
    }
}
