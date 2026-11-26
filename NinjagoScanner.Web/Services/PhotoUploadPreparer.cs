using Microsoft.AspNetCore.Components.Forms;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Shrinks a JPEG or PNG Card Photo in the browser before it is uploaded (web-photo-upload
/// "Photos are downscaled in the browser before upload"), so only the smaller bytes cross the
/// SignalR circuit. Uses Blazor's built-in <c>IBrowserFile.RequestImageFileAsync</c>, which
/// re-encodes through a canvas (same format, original file name, EXIF orientation baked in).
/// Failure-safe: whenever the resize throws, times out, yields an empty file or is not smaller
/// than the original, the original file is returned and a Warning is logged - an upload never
/// fails just because shrinking failed. Holds no per-user state.
/// </summary>
public sealed class PhotoUploadPreparer : IPhotoUploadPreparer
{
    /// <summary>Longest side of an uploaded photo in pixels; larger photos are scaled down, smaller ones never enlarged.</summary>
    public const int MaxLongestSidePixels = 1366;

    internal static readonly TimeSpan DefaultResizeTimeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<PhotoUploadPreparer> logger;
    private readonly TimeSpan resizeTimeout;
    private readonly ResizeImage resizeImage;

    /// <summary>
    /// The browser-side resize. A seam for tests: Blazor's <c>RequestImageFileAsync</c> is an
    /// extension method that refuses any <see cref="IBrowserFile"/> other than its own type.
    /// </summary>
    internal delegate ValueTask<IBrowserFile> ResizeImage(IBrowserFile file, string format, int maxWidth, int maxHeight);

    public PhotoUploadPreparer(ILogger<PhotoUploadPreparer> logger)
        : this(logger, DefaultResizeTimeout, static (file, format, maxWidth, maxHeight) => file.RequestImageFileAsync(format, maxWidth, maxHeight))
    {
    }

    internal PhotoUploadPreparer(ILogger<PhotoUploadPreparer> logger, TimeSpan resizeTimeout, ResizeImage resizeImage)
    {
        this.logger = logger;
        this.resizeTimeout = resizeTimeout;
        this.resizeImage = resizeImage;
    }

    /// <inheritdoc />
    public async Task<IBrowserFile> PrepareAsync(IBrowserFile original, CancellationToken cancellationToken = default)
    {
        var format = GetResizeFormat(original.Name);
        if (format is null)
        {
            return original;
        }

        IBrowserFile resized;
        try
        {
            resized = await resizeImage(original, format, MaxLongestSidePixels, MaxLongestSidePixels)
                .AsTask()
                .WaitAsync(resizeTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Photo downscaling did not finish within {TimeoutSeconds} s; uploading the original.", resizeTimeout.TotalSeconds);
            return original;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Photo downscaling failed; uploading the original.");
            return original;
        }

        if (resized.Size <= 0)
        {
            logger.LogWarning("Photo downscaling produced an empty file; uploading the original.");
            return original;
        }

        if (resized.Size >= original.Size)
        {
            logger.LogWarning(
                "Photo downscaling did not make the file smaller ({ResizedBytes} >= {OriginalBytes} bytes); uploading the original.",
                resized.Size,
                original.Size);
            return original;
        }

        return resized;
    }

    private static string? GetResizeFormat(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => null
        };
    }
}
