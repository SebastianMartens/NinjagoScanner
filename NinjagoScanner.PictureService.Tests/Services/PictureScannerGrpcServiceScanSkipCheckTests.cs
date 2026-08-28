using NinjagoScanner.PictureService;
using NinjagoScanner.PictureService.Services;

namespace NinjagoScanner.PictureService.Tests.Services;

/// <summary>
/// Covers Scan's per-photo skip/retry decision in isolation. The rest of Scan's batch logic
/// (calling Gemini, aborting early on a transport failure) isn't independently testable here:
/// it requires a reachable CatalogService and a real Gemini endpoint, which this test project
/// has no fake/injectable stand-in for (the same pre-existing constraint UploadPhoto's tests
/// work around by only asserting behavior up to that boundary). See tasks.md for this change.
/// </summary>
public sealed class PictureScannerGrpcServiceScanSkipCheckTests
{
    [Fact]
    public void ShouldSkipExistingSidecar_ReturnsFalse_WhenNoSidecarExists()
    {
        Assert.False(PictureScannerGrpcService.ShouldSkipExistingSidecar(existing: null, overwriteExistingSidecars: false));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("uncertain")]
    public void ShouldSkipExistingSidecar_ReturnsTrue_ForCompletedAnalysis_WithoutOverwrite(string analysisStatus)
    {
        var existing = new SidecarRecord { AnalysisStatus = analysisStatus };

        Assert.True(PictureScannerGrpcService.ShouldSkipExistingSidecar(existing, overwriteExistingSidecars: false));
    }

    [Fact]
    public void ShouldSkipExistingSidecar_ReturnsFalse_ForFailedAnalysis_WithoutOverwrite()
    {
        var existing = new SidecarRecord { AnalysisStatus = "failed" };

        Assert.False(PictureScannerGrpcService.ShouldSkipExistingSidecar(existing, overwriteExistingSidecars: false));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("uncertain")]
    [InlineData("failed")]
    public void ShouldSkipExistingSidecar_ReturnsFalse_WhenOverwriteRequested(string analysisStatus)
    {
        var existing = new SidecarRecord { AnalysisStatus = analysisStatus };

        Assert.False(PictureScannerGrpcService.ShouldSkipExistingSidecar(existing, overwriteExistingSidecars: true));
    }
}
