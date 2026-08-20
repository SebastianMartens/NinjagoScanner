using System.Net;
using System.Net.Http.Json;
using NinjagoScanner.Web.Shared.Models;

namespace NinjagoScanner.Web.Client.Services;

/// <summary>
/// Client-side replacement for the former in-process gRPC-calling NinjagoScanner.Web/Services/CardCatalogService.cs.
/// Talks to the BFF's HTTP/JSON API instead of calling CatalogService/PictureService directly —
/// the WASM client has no gRPC access, and no server-held session to route through.
/// </summary>
internal sealed class CardCatalogService(HttpClient httpClient)
{
    public async Task<CollectionOverviewResult> GetCollectionOverviewAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<CollectionOverviewResult>("api/collection/overview", cancellationToken)
            ?? new CollectionOverviewResult { Cards = Array.Empty<CollectionCardItem>() };
    }

    public async Task<CollectionCardDetails?> GetCollectionCardDetailsAsync(string series, string cardNumber, CancellationToken cancellationToken = default)
    {
        var url = $"api/collection/details?series={Uri.EscapeDataString(series)}&cardNumber={Uri.EscapeDataString(cardNumber)}";
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CollectionCardDetails>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<GalleryCardItem>> GetGalleryCardsAsync(string series, CancellationToken cancellationToken = default)
    {
        var url = $"api/gallery?series={Uri.EscapeDataString(series)}";
        return await httpClient.GetFromJsonAsync<IReadOnlyList<GalleryCardItem>>(url, cancellationToken)
            ?? Array.Empty<GalleryCardItem>();
    }

    public async Task<SeriesSummaryResult> GetSeriesSummaryAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<SeriesSummaryResult>("api/series-summary", cancellationToken)
            ?? new SeriesSummaryResult { Series = Array.Empty<SeriesSummaryItem>() };
    }

    public async Task<IReadOnlyList<CardReviewGroup>> GetReviewGroupsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<IReadOnlyList<CardReviewGroup>>("api/review-groups", cancellationToken)
            ?? Array.Empty<CardReviewGroup>();
    }

    public async Task<IReadOnlyList<string>> GetKnownSeriesAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<IReadOnlyList<string>>("api/series", cancellationToken)
            ?? Array.Empty<string>();
    }

    public async Task<long> GetMaxUploadBytesAsync(CancellationToken cancellationToken = default)
    {
        var limits = await httpClient.GetFromJsonAsync<UploadLimitsDto>("api/uploads/limits", cancellationToken);
        return limits?.MaxUploadBytes ?? 15 * 1024 * 1024;
    }

    public async Task UpdateCardSidecarAsync(string photoId, CollectionCardSidecarUpdate update, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"api/photos/{Uri.EscapeDataString(photoId)}/sidecar", update, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateReviewStatusAsync(string photoId, string reviewStatus, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/photos/{Uri.EscapeDataString(photoId)}/review-status",
            new UpdateReviewStatusRequestDto { ReviewStatus = reviewStatus },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateSetNameAsync(string photoId, string? setName, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/photos/{Uri.EscapeDataString(photoId)}/set-name",
            new UpdateSetNameRequestDto { SetName = setName },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCardNumberAsync(string photoId, string? cardNumber, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/photos/{Uri.EscapeDataString(photoId)}/card-number",
            new UpdateCardNumberRequestDto { CardNumber = cardNumber },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCardLanguageAsync(string photoId, string? language, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"api/photos/{Uri.EscapeDataString(photoId)}/language",
            new UpdateCardLanguageRequestDto { Language = language },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeletePhotoAsync(string photoId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"api/photos/{Uri.EscapeDataString(photoId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Step 1 of the direct-to-S3 upload flow: asks the BFF to validate the candidate file and
    /// issue a pre-authorized upload URL. Throws <see cref="InvalidOperationException"/> with the
    /// BFF's message if the file is rejected (too large / unsupported type).
    /// </summary>
    public async Task<UploadUrlResponseDto> RequestUploadUrlAsync(string fileName, long fileSizeBytes, string? contentType, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "api/uploads",
            new UploadUrlRequestDto { FileName = fileName, FileSizeBytes = fileSizeBytes, ContentType = contentType },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(message);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<UploadUrlResponseDto>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Der Server hat keine Upload-URL geliefert.");
    }

    /// <summary>Step 2 of the direct-to-S3 upload flow: PUTs the photo bytes straight to the pre-authorized S3 URL.</summary>
    public async Task UploadToPresignedUrlAsync(string uploadUrl, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        using var httpContent = new StreamContent(content);
        httpContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = httpContent };
        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Step 3 of the direct-to-S3 upload flow: tells the BFF the upload finished, triggering AI Analysis.</summary>
    public async Task<CardListItem> ConfirmUploadAsync(string photoId, string sourceFileName, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"api/uploads/{Uri.EscapeDataString(photoId)}/confirm",
            new ConfirmUploadRequestDto { SourceFileName = sourceFileName },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CardListItem>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Der Server hat kein Analyseergebnis geliefert.");
    }
}
