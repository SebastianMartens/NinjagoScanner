namespace NinjagoScanner.Web.Shared.Models;

/// <summary>
/// Request body for POST /api/uploads: asks the BFF to validate a candidate upload and, if
/// accepted, issue a short-lived pre-authorized S3 upload URL for it.
/// </summary>
public sealed class UploadUrlRequestDto
{
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public string? ContentType { get; init; }
}

/// <summary>Response for POST /api/uploads: the generated photo ID and the pre-signed PUT URL to upload bytes to.</summary>
public sealed class UploadUrlResponseDto
{
    public required string PhotoId { get; init; }
    public required string UploadUrl { get; init; }
}

/// <summary>
/// Request body for POST /api/uploads/{photoId}/confirm, sent once the browser has finished
/// PUTting the photo bytes directly to the pre-signed URL.
/// </summary>
public sealed class ConfirmUploadRequestDto
{
    public required string SourceFileName { get; init; }
}
