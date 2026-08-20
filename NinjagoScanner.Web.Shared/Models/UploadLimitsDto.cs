namespace NinjagoScanner.Web.Shared.Models;

/// <summary>Response for GET /api/uploads/limits — lets the client display the configured max upload size.</summary>
public sealed class UploadLimitsDto
{
    public required long MaxUploadBytes { get; init; }
}
