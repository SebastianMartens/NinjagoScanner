using Amazon.S3;
using Amazon.S3.Model;

namespace NinjagoScanner.Web.Bff;

/// <summary>
/// Seam over S3 URL pre-signing, so tests can substitute a fake instead of needing real AWS
/// credentials. Implemented against S3 by <see cref="S3UploadUrlIssuer"/>.
/// </summary>
internal interface IUploadUrlIssuer
{
    Task<string> CreateUploadUrlAsync(string photoId, string contentType, CancellationToken cancellationToken);

    Task<string> CreateDownloadUrlAsync(string photoId, CancellationToken cancellationToken);
}

/// <summary>
/// Issues short-lived pre-signed S3 URLs for the photos bucket configured via
/// <see cref="BffConfig.ResolvePhotosBucketName"/>. The BFF never touches photo bytes itself —
/// the browser PUTs directly to the upload URL, and GETs images directly from the download URL.
/// </summary>
internal sealed class S3UploadUrlIssuer : IUploadUrlIssuer
{
    private static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromHours(1);

    private readonly IAmazonS3 s3Client;
    private readonly string bucketName;

    public S3UploadUrlIssuer(IAmazonS3 s3Client, string bucketName)
    {
        this.s3Client = s3Client;
        this.bucketName = bucketName;
    }

    public Task<string> CreateUploadUrlAsync(string photoId, string contentType, CancellationToken cancellationToken)
    {
        return s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = PhotoObjectKeys.Build(photoId),
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(UploadUrlLifetime)
        });
    }

    public Task<string> CreateDownloadUrlAsync(string photoId, CancellationToken cancellationToken)
    {
        return s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = PhotoObjectKeys.Build(photoId),
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(DownloadUrlLifetime)
        });
    }
}
