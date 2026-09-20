using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Hosts a hand-written, in-process fake of <c>CardPictureService</c> over cleartext HTTP/2 on a
/// loopback port, backed by in-memory photo/sidecar stores, so tests can point the Web app's
/// gRPC-calling logic at a real gRPC endpoint without a separately-run process or real AWS
/// credentials. The real implementation is `picture_service/` (Python, a separate service - see
/// openspec/changes/picture-service-python-rewrite/design.md); the `.proto` file is the only
/// contract shared with it.
/// </summary>
public sealed class PictureServiceTestHost : IAsyncDisposable
{
    // ListCards only recognizes photos that exist in the photo store; content is irrelevant.
    private static readonly byte[] DummyImageBytes = [0xFF, 0xD8, 0xFF, 0xD9];

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly InMemoryPhotoStore photoStore = new();
    private readonly InMemorySidecarStore sidecarStore = new();
    private readonly ReanalysisSettings reanalysisSettings = new();
    private readonly CallLog callLog = new();

    private WebApplication? app;

    public string Address { get; private set; } = string.Empty;

    /// <summary>
    /// When set, the fake's <c>ReanalyzePhoto</c> fails with this status instead of re-analyzing
    /// (e.g. <see cref="StatusCode.Unavailable"/> to mimic Gemini being down), leaving the
    /// photo's sidecar untouched, as the real service does.
    /// </summary>
    public StatusCode? ReanalyzeFailureStatusCode
    {
        get => reanalysisSettings.FailureStatusCode;
        set => reanalysisSettings.FailureStatusCode = value;
    }

    /// <summary>
    /// What the fake's <c>ReanalyzePhoto</c> writes to a photo's sidecar (with analysis status
    /// <c>ok</c>); the photo's existing review status is preserved.
    /// </summary>
    public void SetReanalysisResult(string cardName, string cardNumber, string setName)
    {
        reanalysisSettings.CardName = cardName;
        reanalysisSettings.CardNumber = cardNumber;
        reanalysisSettings.SetName = setName;
    }

    /// <summary>Every <c>UploadPhoto</c> call received, in order, with whether it set <c>skip_analysis</c>.</summary>
    public IReadOnlyList<RecordedUpload> Uploads => callLog.Uploads.ToArray();

    /// <summary>How many <c>GetPhotoDownloadUrl</c> calls were received.</summary>
    public int DownloadUrlCallCount => callLog.DownloadUrlCallCount;

    public void WritePhoto(string photoId, string? sidecarJson = null, string? collectionId = null)
    {
        var resolvedCollectionId = collectionId ?? TestCurrentCollectionContext.CollectionId;
        photoStore.Seed(resolvedCollectionId, photoId, DummyImageBytes);

        if (sidecarJson is not null)
        {
            var record = JsonSerializer.Deserialize<FakeSidecarRecord>(sidecarJson, JsonOptions);
            if (record is not null)
            {
                sidecarStore.Seed(resolvedCollectionId, photoId, record);
            }
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddGrpc();
        builder.Services.AddSingleton(photoStore);
        builder.Services.AddSingleton(sidecarStore);
        builder.Services.AddSingleton(reanalysisSettings);
        builder.Services.AddSingleton(callLog);
        builder.Services.AddScoped<FakeCardPictureService>();

        app = builder.Build();
        app.MapGrpcService<FakeCardPictureService>();

        await app.StartAsync();

        var addressesFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        Address = addressesFeature!.Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private sealed class InMemoryPhotoStore
    {
        private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), byte[]> objects = new();

        public void Seed(string collectionId, string photoId, byte[] bytes) => objects[(collectionId, photoId)] = bytes;

        public void Put(string collectionId, string photoId, byte[] bytes) => objects[(collectionId, photoId)] = bytes;

        public string CreateDownloadUrl(string collectionId, string photoId) => $"https://fake-photo-store.test/{photoId}";

        public bool Exists(string collectionId, string photoId) => objects.ContainsKey((collectionId, photoId));

        public void Delete(string collectionId, string photoId) => objects.TryRemove((collectionId, photoId), out _);

        public IEnumerable<string> ListPhotoIds(string collectionId)
        {
            return objects.Keys
                .Where(key => key.CollectionId == collectionId)
                .Select(key => key.PhotoId)
                .OrderBy(photoId => photoId, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class InMemorySidecarStore
    {
        private readonly ConcurrentDictionary<(string CollectionId, string PhotoId), FakeSidecarRecord> records = new();

        public void Seed(string collectionId, string photoId, FakeSidecarRecord record) => records[(collectionId, photoId)] = record;

        public FakeSidecarRecord? Get(string collectionId, string photoId) =>
            records.TryGetValue((collectionId, photoId), out var record) ? record : null;

        public void Put(string collectionId, string photoId, FakeSidecarRecord record) => records[(collectionId, photoId)] = record;

        public void Delete(string collectionId, string photoId) => records.TryRemove((collectionId, photoId), out _);

        public IEnumerable<(string PhotoId, FakeSidecarRecord Record)> ListByCollection(string collectionId)
        {
            return records
                .Where(pair => pair.Key.CollectionId == collectionId)
                .Select(pair => (pair.Key.PhotoId, pair.Value));
        }
    }

    public sealed record RecordedUpload(string SourceFileName, bool SkipAnalysis);

    private sealed class CallLog
    {
        private int downloadUrlCallCount;

        public ConcurrentQueue<RecordedUpload> Uploads { get; } = new();

        public int DownloadUrlCallCount => Volatile.Read(ref downloadUrlCallCount);

        public void RecordDownloadUrlCall() => Interlocked.Increment(ref downloadUrlCallCount);
    }

    private sealed class ReanalysisSettings
    {
        public StatusCode? FailureStatusCode { get; set; }
        public string CardName { get; set; } = "Reanalyzed";
        public string CardNumber { get; set; } = "99";
        public string SetName { get; set; } = "Serie 2";
    }

    /// <summary>
    /// Lenient, fully-optional sidecar shape mirroring the real service's DynamoDB item fields -
    /// only what this fake and its tests' seed JSON actually need.
    /// </summary>
    private sealed record FakeSidecarRecord
    {
        public string? AnalysisStatus { get; init; }
        public string? ReviewStatus { get; init; }
        public string? CardName { get; init; }
        public string? CardNumber { get; init; }
        public string? SetName { get; init; }
        public string? Rarity { get; init; }
        public string? Language { get; init; }
        public string? SourceFileName { get; init; }
    }

    /// <summary>
    /// Hand-written fake implementing the generated <c>CardPictureServiceBase</c> directly,
    /// covering the same RPC surface as the real service with simplified (no real Gemini/catalog
    /// call) behavior - enough for Web's tests to exercise realistic gRPC request/response shapes
    /// without depending on PictureService's language, runtime, or external services.
    /// </summary>
    private sealed class FakeCardPictureService : CardPictureService.CardPictureServiceBase
    {
        private readonly InMemoryPhotoStore photoStore;
        private readonly InMemorySidecarStore sidecarStore;
        private readonly ReanalysisSettings reanalysisSettings;
        private readonly CallLog callLog;

        public FakeCardPictureService(
            InMemoryPhotoStore photoStore,
            InMemorySidecarStore sidecarStore,
            ReanalysisSettings reanalysisSettings,
            CallLog callLog)
        {
            this.photoStore = photoStore;
            this.sidecarStore = sidecarStore;
            this.reanalysisSettings = reanalysisSettings;
            this.callLog = callLog;
        }

        public override Task<ScanSummary> Scan(ScanRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);

            var photoIds = photoStore.ListPhotoIds(request.CollectionId).ToList();
            var processed = 0;

            foreach (var photoId in photoIds)
            {
                if (sidecarStore.Get(request.CollectionId, photoId) is not null)
                {
                    continue;
                }

                sidecarStore.Put(request.CollectionId, photoId, new FakeSidecarRecord { AnalysisStatus = "ok" });
                processed++;
            }

            return Task.FromResult(new ScanSummary
            {
                TotalImages = photoIds.Count,
                Processed = processed,
                Message = "Batch abgeschlossen."
            });
        }

        public override async Task<UploadPhotoResponse> UploadPhoto(
            IAsyncStreamReader<UploadPhotoRequest> requestStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext(context.CancellationToken)
                || requestStream.Current.PayloadCase != UploadPhotoRequest.PayloadOneofCase.Metadata)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Der Upload-Stream muss mit einer Metadaten-Nachricht beginnen."));
            }

            var metadata = requestStream.Current.Metadata;
            EnsureCollectionId(metadata.CollectionId);

            using var buffer = new MemoryStream();
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                buffer.Write(requestStream.Current.Chunk.Span);
            }

            if (buffer.Length == 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Die hochgeladene Datei ist leer."));
            }

            var photoId = Guid.NewGuid().ToString("n");
            photoStore.Put(metadata.CollectionId, photoId, buffer.ToArray());

            callLog.Uploads.Enqueue(new RecordedUpload(metadata.SourceFileName, metadata.SkipAnalysis));

            var record = new FakeSidecarRecord
            {
                AnalysisStatus = metadata.SkipAnalysis ? "notAnalyzed" : "ok",
                SourceFileName = metadata.SourceFileName
            };
            sidecarStore.Put(metadata.CollectionId, photoId, record);

            return new UploadPhotoResponse { Card = ToCardEntry(photoId, record) };
        }

        public override Task<GetPhotoDownloadUrlResponse> GetPhotoDownloadUrl(GetPhotoDownloadUrlRequest request, ServerCallContext context)
        {
            callLog.RecordDownloadUrlCall();
            EnsureCollectionId(request.CollectionId);

            if (!photoStore.Exists(request.CollectionId, request.PhotoId))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Foto '{request.PhotoId}' wurde im Speicher nicht gefunden."));
            }

            return Task.FromResult(new GetPhotoDownloadUrlResponse
            {
                DownloadUrl = photoStore.CreateDownloadUrl(request.CollectionId, request.PhotoId)
            });
        }

        public override Task<ListCardsResponse> ListCards(ListCardsRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);

            var response = new ListCardsResponse();
            foreach (var photoId in photoStore.ListPhotoIds(request.CollectionId))
            {
                var record = sidecarStore.Get(request.CollectionId, photoId);
                response.Cards.Add(ToCardEntry(photoId, record, photoStore.CreateDownloadUrl(request.CollectionId, photoId)));
            }

            return Task.FromResult(response);
        }

        public override Task<ListSourceFileNamesResponse> ListSourceFileNames(ListSourceFileNamesRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);

            var response = new ListSourceFileNamesResponse();
            response.SourceFileNames.AddRange(sidecarStore.ListByCollection(request.CollectionId)
                .Select(entry => entry.Record.SourceFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)!);

            return Task.FromResult(response);
        }

        public override Task<GetCardDetailsResponse> GetCardDetails(GetCardDetailsRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);
            var record = sidecarStore.Get(request.CollectionId, request.PhotoId);

            var details = new CardDetails
            {
                PhotoId = request.PhotoId,
                ScannedAtUtc = string.Empty
            };
            return Task.FromResult(new GetCardDetailsResponse { Details = details });
        }

        public override Task<UpdateSidecarResponse> UpdateSidecar(UpdateSidecarRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);
            sidecarStore.Put(request.CollectionId, request.PhotoId, new FakeSidecarRecord
            {
                AnalysisStatus = NormalizeNullable(request.AnalysisStatus),
                CardName = NormalizeNullable(request.CardName),
                CardNumber = NormalizeNullable(request.CardNumber),
                SetName = NormalizeNullable(request.SetName),
                Rarity = NormalizeNullable(request.Rarity),
                Language = NormalizeNullable(request.Language),
                ReviewStatus = NormalizeNullable(request.ReviewStatus)
            });
            return Task.FromResult(new UpdateSidecarResponse { Success = true });
        }

        public override Task<UpdateSetNameResponse> UpdateSetName(UpdateSetNameRequest request, ServerCallContext context)
        {
            ApplySingleFieldUpdate(request.CollectionId, request.PhotoId, record => record with { SetName = NormalizeNullable(request.SetName) });
            return Task.FromResult(new UpdateSetNameResponse { Success = true });
        }

        public override Task<UpdateCardNumberResponse> UpdateCardNumber(UpdateCardNumberRequest request, ServerCallContext context)
        {
            ApplySingleFieldUpdate(request.CollectionId, request.PhotoId, record => record with { CardNumber = NormalizeNullable(request.CardNumber) });
            return Task.FromResult(new UpdateCardNumberResponse { Success = true });
        }

        public override Task<UpdateCardLanguageResponse> UpdateCardLanguage(UpdateCardLanguageRequest request, ServerCallContext context)
        {
            ApplySingleFieldUpdate(request.CollectionId, request.PhotoId, record => record with { Language = NormalizeNullable(request.Language) });
            return Task.FromResult(new UpdateCardLanguageResponse { Success = true });
        }

        public override Task<UpdateReviewStatusResponse> UpdateReviewStatus(UpdateReviewStatusRequest request, ServerCallContext context)
        {
            ApplySingleFieldUpdate(request.CollectionId, request.PhotoId, record => record with { ReviewStatus = NormalizeNullable(request.ReviewStatus) });
            return Task.FromResult(new UpdateReviewStatusResponse { Success = true });
        }

        public override Task<MigrateSidecarsResponse> MigrateSidecars(MigrateSidecarsRequest request, ServerCallContext context)
        {
            // No legacy-shaped records are ever seeded by this fake; nothing to migrate.
            return Task.FromResult(new MigrateSidecarsResponse());
        }

        public override Task<DeletePhotoResponse> DeletePhoto(DeletePhotoRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);

            if (!photoStore.Exists(request.CollectionId, request.PhotoId))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Das Foto '{request.PhotoId}' wurde nicht gefunden."));
            }

            photoStore.Delete(request.CollectionId, request.PhotoId);
            sidecarStore.Delete(request.CollectionId, request.PhotoId);

            return Task.FromResult(new DeletePhotoResponse { Success = true });
        }

        public override Task<ReanalyzePhotoResponse> ReanalyzePhoto(ReanalyzePhotoRequest request, ServerCallContext context)
        {
            EnsureCollectionId(request.CollectionId);

            if (!photoStore.Exists(request.CollectionId, request.PhotoId))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Foto '{request.PhotoId}' wurde im Speicher nicht gefunden."));
            }

            if (reanalysisSettings.FailureStatusCode is { } failureStatusCode)
            {
                throw new RpcException(new Status(failureStatusCode, "Die Analyse konnte nicht ausgefuehrt werden."));
            }

            var existing = sidecarStore.Get(request.CollectionId, request.PhotoId) ?? new FakeSidecarRecord();
            var updated = existing with
            {
                AnalysisStatus = "ok",
                CardName = reanalysisSettings.CardName,
                CardNumber = reanalysisSettings.CardNumber,
                SetName = reanalysisSettings.SetName
            };
            sidecarStore.Put(request.CollectionId, request.PhotoId, updated);

            return Task.FromResult(new ReanalyzePhotoResponse { Card = ToCardEntry(request.PhotoId, updated) });
        }

        private void ApplySingleFieldUpdate(string collectionId, string photoId, Func<FakeSidecarRecord, FakeSidecarRecord> apply)
        {
            EnsureCollectionId(collectionId);
            var existing = sidecarStore.Get(collectionId, photoId) ?? new FakeSidecarRecord();
            sidecarStore.Put(collectionId, photoId, apply(existing));
        }

        private static void EnsureCollectionId(string collectionId)
        {
            if (string.IsNullOrWhiteSpace(collectionId))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Keine collection_id angegeben."));
            }
        }

        private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static CardEntry ToCardEntry(string photoId, FakeSidecarRecord? record, string downloadUrl = "")
        {
            return new CardEntry
            {
                PhotoId = photoId,
                SourceFileName = record?.SourceFileName ?? string.Empty,
                AnalysisStatus = record?.AnalysisStatus ?? "notAnalyzed",
                CardName = record?.CardName ?? string.Empty,
                CardNumber = record?.CardNumber ?? string.Empty,
                SetName = record?.SetName ?? string.Empty,
                Rarity = record?.Rarity ?? string.Empty,
                Language = record?.Language ?? "de",
                ReviewStatus = record?.ReviewStatus ?? "unreviewed",
                DownloadUrl = downloadUrl
            };
        }
    }
}
