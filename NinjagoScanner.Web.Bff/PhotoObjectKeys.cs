namespace NinjagoScanner.Web.Bff;

/// <summary>
/// Builds the S3 object key for a photo ID. Must stay in sync with PictureService's
/// <c>PhotoStore.BuildObjectKey</c> — the BFF issues pre-signed URLs against the same bucket
/// PictureService reads from, so both sides need to agree on the key layout.
/// </summary>
internal static class PhotoObjectKeys
{
    private const string Prefix = "photos/";

    public static string Build(string photoId) => $"{Prefix}{photoId}";
}
