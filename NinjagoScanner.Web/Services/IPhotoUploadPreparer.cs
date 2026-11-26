using Microsoft.AspNetCore.Components.Forms;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// Decides which file the /upload page actually uploads for a selected photo. An interface so the
/// page can be tested with a stub (the real implementation needs a browser).
/// </summary>
public interface IPhotoUploadPreparer
{
    /// <summary>Returns the file to upload: the shrunk copy when that worked and is smaller, otherwise <paramref name="original"/>.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IBrowserFile> PrepareAsync(IBrowserFile original, CancellationToken cancellationToken = default);
}
