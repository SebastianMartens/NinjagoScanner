using NinjagoScanner.Web.Bff;

namespace NinjagoScanner.Web.Bff.Tests.Fixtures;

/// <summary>
/// Deterministic stand-in for <see cref="IUploadUrlIssuer"/> so tests can exercise the BFF's
/// query/update logic without real AWS credentials — see <see cref="S3UploadUrlIssuer"/> for the
/// production implementation this substitutes.
/// </summary>
internal sealed class FakeUploadUrlIssuer : IUploadUrlIssuer
{
    public Task<string> CreateUploadUrlAsync(string photoId, string contentType, CancellationToken cancellationToken)
    {
        return Task.FromResult($"https://fake-s3.test/photos/{photoId}?upload&contentType={Uri.EscapeDataString(contentType)}");
    }

    public Task<string> CreateDownloadUrlAsync(string photoId, CancellationToken cancellationToken)
    {
        return Task.FromResult($"https://fake-s3.test/photos/{photoId}?download");
    }
}
