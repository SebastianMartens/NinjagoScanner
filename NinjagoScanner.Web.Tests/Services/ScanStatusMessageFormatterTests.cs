using NinjagoScanner.Web.Models;
using NinjagoScanner.Web.Services;

namespace NinjagoScanner.Web.Tests.Services;

public sealed class ScanStatusMessageFormatterTests
{
    [Fact]
    public void BuildMessage_ReturnsConfigurationErrorMessage_WhenHasConfigurationError()
    {
        var summary = new ScanSummaryDto { HasConfigurationError = true, Message = "GEMINI_API_KEY ist nicht gesetzt." };

        var message = ScanStatusMessageFormatter.BuildMessage(summary);

        Assert.Equal("GEMINI_API_KEY ist nicht gesetzt.", message);
    }

    [Fact]
    public void BuildMessage_ReturnsCounts_WhenScanCompletesNormally()
    {
        var summary = new ScanSummaryDto { Processed = 10, Skipped = 2, Uncertain = 1, Failed = 3, StoppedEarly = false };

        var message = ScanStatusMessageFormatter.BuildMessage(summary);

        Assert.Equal("Scan fertig: 10 verarbeitet, 2 uebersprungen, 1 unsicher, 3 fehlgeschlagen.", message);
    }

    [Fact]
    public void BuildMessage_IndicatesEarlyStop_WhenStoppedEarly()
    {
        var summary = new ScanSummaryDto { Processed = 5, Skipped = 0, Uncertain = 0, Failed = 1, StoppedEarly = true };

        var message = ScanStatusMessageFormatter.BuildMessage(summary);

        Assert.Contains("vorzeitig abgebrochen", message);
        Assert.Contains("5 verarbeitet, 0 uebersprungen, 0 unsicher, 1 fehlgeschlagen.", message);
    }
}
