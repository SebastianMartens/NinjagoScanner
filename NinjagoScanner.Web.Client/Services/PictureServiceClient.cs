using System.Net.Http.Json;
using NinjagoScanner.Web.Shared.Models;

namespace NinjagoScanner.Web.Client.Services;

/// <summary>Client-side wrapper for the BFF's bulk-scan endpoint, mirroring the former PictureServiceClient.cs.</summary>
internal sealed class PictureServiceClient(HttpClient httpClient)
{
    public async Task<ScanSummaryDto> ScanAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync("api/scan", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScanSummaryDto>(cancellationToken: cancellationToken)
            ?? new ScanSummaryDto { HasConfigurationError = true, Message = "Keine Antwort vom Server." };
    }
}
