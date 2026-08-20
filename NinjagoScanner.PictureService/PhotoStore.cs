using Amazon.S3;
using Amazon.S3.Model;

namespace NinjagoScanner.PictureService;

/// <summary>
/// Storage seam for card photo bytes, keyed by generated photo ID. Implemented against S3 by
/// <see cref="PhotoStore"/>; tests substitute an in-memory fake instead of mocking the AWS SDK.
/// </summary>
internal interface IPhotoStore
{
    Task<byte[]> GetBytesAsync(string photoId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string photoId, CancellationToken cancellationToken);

    Task DeleteAsync(string photoId, CancellationToken cancellationToken);

    IAsyncEnumerable<string> ListPhotoIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads and deletes card photo bytes in the S3 bucket configured via
/// <see cref="ScannerConfig.ResolvePhotosBucketName"/>, keyed by generated photo ID.
/// Uploads themselves happen browser-to-S3 via a pre-authorized URL issued by the Web BFF —
/// this service never writes photo bytes, only reads/deletes them.
/// </summary>
internal sealed class PhotoStore : IPhotoStore
{
    private const string KeyPrefix = "photos/";

    private readonly IAmazonS3 s3Client;
    private readonly string bucketName;

    public PhotoStore(IAmazonS3 s3Client, string bucketName)
    {
        this.s3Client = s3Client;
        this.bucketName = bucketName;
    }

    public static string BuildObjectKey(string photoId) => $"{KeyPrefix}{photoId}";

    public async Task<byte[]> GetBytesAsync(string photoId, CancellationToken cancellationToken)
    {
        using var response = await s3Client.GetObjectAsync(bucketName, BuildObjectKey(photoId), cancellationToken);
        using var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
        return memoryStream.ToArray();
    }

    public async Task<bool> ExistsAsync(string photoId, CancellationToken cancellationToken)
    {
        try
        {
            await s3Client.GetObjectMetadataAsync(bucketName, BuildObjectKey(photoId), cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string photoId, CancellationToken cancellationToken)
    {
        await s3Client.DeleteObjectAsync(bucketName, BuildObjectKey(photoId), cancellationToken);
    }

    /// <summary>
    /// Lists every photo ID currently stored in the bucket. Used by the bulk <c>Scan</c> RPC to
    /// find photos without a sidecar record yet.
    /// </summary>
    public async IAsyncEnumerable<string> ListPhotoIdsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = bucketName,
            Prefix = KeyPrefix
        };

        ListObjectsV2Response response;
        do
        {
            response = await s3Client.ListObjectsV2Async(request, cancellationToken);
            foreach (var entry in response.S3Objects)
            {
                yield return entry.Key[KeyPrefix.Length..];
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated == true);
    }
}
