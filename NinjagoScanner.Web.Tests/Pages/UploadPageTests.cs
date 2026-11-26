using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NinjagoScanner.Web.Components.Pages;
using NinjagoScanner.Web.Services;
using NinjagoScanner.Web.Tests.Fixtures;

namespace NinjagoScanner.Web.Tests.Pages;

/// <summary>
/// Page wiring of /upload around <see cref="IPhotoUploadPreparer"/> (web-photo-upload "File type
/// and size validation before upload"): the page must upload the file the preparer returns and
/// validate that file's size, not the originally selected one. The preparer is stubbed (the real
/// resize needs a browser); the upload goes through the real PictureServiceClient to the fake
/// PictureService.
/// </summary>
public sealed class UploadPageTests : IAsyncLifetime
{
    private const long MaxUploadBytes = 1024;
    private const int OriginalBytes = 4096;
    private const int ShrunkBytes = 512;

    private readonly PictureServiceTestHost pictureHost = new();
    private readonly StubPhotoUploadPreparer preparer = new();
    private readonly BunitContext ctx = new();
    private TestGamificationServiceHandle gamification = null!;

    static UploadPageTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await pictureHost.StartAsync();

        // The catalog is never reached: the page's post-upload gamification/celebration lookups are
        // best-effort and swallow failures.
        const string unreachableCatalog = "http://localhost:0";
        var pictureClient = TestPictureServiceClientFactory.Create(pictureHost.Address, unreachableCatalog, MaxUploadBytes);
        gamification = await TestGamificationServiceFactory.CreateAsync(unreachableCatalog, pictureHost.Address, MaxUploadBytes);

        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(pictureClient);
        ctx.Services.AddSingleton(new CollectionQueryService(new CatalogServiceClient(unreachableCatalog), pictureClient));
        ctx.Services.AddSingleton(gamification.Service);
        ctx.Services.AddSingleton(new GamificationCelebrationCenter());
        ctx.Services.AddSingleton<IPhotoUploadPreparer>(preparer);
    }

    public async Task DisposeAsync()
    {
        await ctx.DisposeAsync();
        await gamification.DisposeAsync();
        await pictureHost.DisposeAsync();
    }

    private static InputFileContent Photo(string name, int bytes) =>
        InputFileContent.CreateFromBinary(new byte[bytes], name);

    private static FakeBrowserFile ShrunkCopyOf(IBrowserFile original) =>
        new(original.Name, ShrunkBytes, content: new byte[ShrunkBytes]);

    [Fact]
    public void Batch_UploadsTheShrunkPhoto_WhenTheOriginalExceedsTheSizeLimit()
    {
        preparer.Prepare = ShrunkCopyOf;
        var cut = ctx.Render<Upload>();

        cut.FindComponents<InputFile>()[1].UploadFiles(Photo("IMG_0001.jpg", OriginalBytes));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Fotos hochladen")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Batch-Upload abgeschlossen", cut.Markup), TimeSpan.FromSeconds(15));
        Assert.Contains("Hochgeladen: 1", cut.Markup);
        Assert.Contains("Fehlgeschlagen: 0", cut.Markup);
        var upload = Assert.Single(pictureHost.Uploads);
        Assert.Equal("IMG_0001.jpg", upload.SourceFileName);
        Assert.True(upload.SkipAnalysis);
    }

    [Fact]
    public void Batch_RejectsTheOriginalWithTheSizeError_WhenFallingBackToAnOversizedFile()
    {
        preparer.Prepare = original => original;
        var cut = ctx.Render<Upload>();

        cut.FindComponents<InputFile>()[1].UploadFiles(Photo("IMG_0002.jpg", OriginalBytes));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Fotos hochladen")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Batch-Upload abgeschlossen", cut.Markup), TimeSpan.FromSeconds(15));
        Assert.Contains("Hochgeladen: 0", cut.Markup);
        Assert.Contains("Fehlgeschlagen: 1", cut.Markup);
        Assert.Contains("IMG_0002.jpg", cut.Markup);
        Assert.Contains("zu gross", cut.Markup);
        Assert.Empty(pictureHost.Uploads);
    }

    [Fact]
    public void Single_UploadsTheShrunkPhoto_WhenTheOriginalExceedsTheSizeLimit()
    {
        preparer.Prepare = ShrunkCopyOf;
        var cut = ctx.Render<Upload>();

        cut.FindComponents<InputFile>()[0].UploadFiles(Photo("IMG_0003.jpg", OriginalBytes));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Foto hochladen")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Upload erfolgreich: IMG_0003.jpg", cut.Markup), TimeSpan.FromSeconds(15));
        var upload = Assert.Single(pictureHost.Uploads);
        Assert.Equal("IMG_0003.jpg", upload.SourceFileName);
        Assert.False(upload.SkipAnalysis);
    }

    [Fact]
    public void Single_RejectsTheOriginal_WhenFallingBackToAnOversizedFile()
    {
        preparer.Prepare = original => original;
        var cut = ctx.Render<Upload>();

        cut.FindComponents<InputFile>()[0].UploadFiles(Photo("IMG_0004.jpg", OriginalBytes));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Foto hochladen")).Click();

        cut.WaitForAssertion(() => Assert.Contains("Upload fehlgeschlagen", cut.Markup), TimeSpan.FromSeconds(15));
        Assert.DoesNotContain("Upload erfolgreich", cut.Markup);
        Assert.Empty(pictureHost.Uploads);
    }

    private sealed class StubPhotoUploadPreparer : IPhotoUploadPreparer
    {
        public Func<IBrowserFile, IBrowserFile> Prepare { get; set; } = original => original;

        public Task<IBrowserFile> PrepareAsync(IBrowserFile original, CancellationToken cancellationToken = default) =>
            Task.FromResult(Prepare(original));
    }
}
