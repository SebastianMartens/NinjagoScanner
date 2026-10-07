using Grpc.Core;
using Grpc.Net.Client;
using NinjagoScanner.PictureService.Protos;

namespace NinjagoScanner.Web.Tests.Fixtures;

/// <summary>
/// Pins the fake's <c>TransferPhotos</c> to the real service's contract (move semantics,
/// all-or-nothing, idempotency by transfer id) so trade tests built on it are trustworthy.
/// </summary>
public sealed class PictureServiceTestHostTransferTests : IAsyncLifetime
{
    private const string Source = "col-a";
    private const string Dest = "col-b";

    private readonly PictureServiceTestHost host = new();
    private GrpcChannel channel = null!;
    private CardPictureService.CardPictureServiceClient client = null!;

    static PictureServiceTestHostTransferTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    public async Task InitializeAsync()
    {
        await host.StartAsync();
        channel = GrpcChannel.ForAddress(host.Address);
        client = new CardPictureService.CardPictureServiceClient(channel);
    }

    public async Task DisposeAsync()
    {
        channel.Dispose();
        await host.DisposeAsync();
    }

    private static TransferPhotosRequest Request(string transferId, params (string Source, string Dest, string Photo)[] moves)
    {
        var request = new TransferPhotosRequest { TransferId = transferId };
        foreach (var (source, dest, photo) in moves)
        {
            request.Moves.Add(new PhotoMove { SourceCollectionId = source, DestCollectionId = dest, PhotoId = photo });
        }

        return request;
    }

    private async Task<StatusCode> StatusOf(TransferPhotosRequest request)
    {
        var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.TransferPhotosAsync(request));
        return exception.StatusCode;
    }

    private static string Sidecar(string cardName) =>
        $$"""{ "analysisStatus": "ok", "reviewStatus": "verified", "cardName": "{{cardName}}", "cardNumber": "7", "setName": "Serie 1", "language": "en", "sourceFileName": "x.jpg", "rotated180": true }""";

    [Fact]
    public async Task Move_creates_new_ids_in_destination_with_identical_sidecar_and_removes_source()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.WritePhoto("p2", Sidecar("Jay"), Source);

        var response = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1"), (Source, Dest, "p2")));

        Assert.Equal(2, response.Results.Count);
        Assert.Empty(host.PhotoIds(Source));
        Assert.Equal(2, host.PhotoIds(Dest).Count);
        Assert.All(response.Results, r =>
        {
            Assert.NotEqual(r.OldPhotoId, r.NewPhotoId);
            Assert.True(host.HasPhoto(Dest, r.NewPhotoId));
            Assert.Equal(Source, r.SourceCollectionId);
            Assert.Equal(Dest, r.DestCollectionId);
        });

        var destCards = (await client.ListCardsAsync(new ListCardsRequest { CollectionId = Dest })).Cards;
        Assert.Equal(["Jay", "Kai"], destCards.Select(c => c.CardName).Order().ToArray());
        Assert.All(destCards, c =>
        {
            Assert.Equal("verified", c.ReviewStatus);
            Assert.Equal("7", c.CardNumber);
            Assert.Equal("Serie 1", c.SetName);
            Assert.Equal("en", c.Language);
            Assert.Equal("x.jpg", c.SourceFileName);
            Assert.True(c.Rotated180);
        });
        Assert.Empty((await client.ListCardsAsync(new ListCardsRequest { CollectionId = Source })).Cards);
    }

    [Fact]
    public async Task Both_directions_in_one_call_swap_cards()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.WritePhoto("q1", Sidecar("Jay"), Dest);

        await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1"), (Dest, Source, "q1")));

        var sourceCards = (await client.ListCardsAsync(new ListCardsRequest { CollectionId = Source })).Cards;
        var destCards = (await client.ListCardsAsync(new ListCardsRequest { CollectionId = Dest })).Cards;
        Assert.Equal("Jay", Assert.Single(sourceCards).CardName);
        Assert.Equal("Kai", Assert.Single(destCards).CardName);
    }

    [Fact]
    public async Task Unknown_photo_is_not_found_and_nothing_moves()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        var status = await StatusOf(Request("t-1", (Source, Dest, "p1"), (Source, Dest, "missing")));

        Assert.Equal(StatusCode.NotFound, status);
        Assert.True(host.HasPhoto(Source, "p1"));
        Assert.Empty(host.PhotoIds(Dest));
    }

    [Fact]
    public async Task Same_collection_is_invalid_argument()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("t-1", (Source, Source, "p1"))));
        Assert.True(host.HasPhoto(Source, "p1"));
    }

    [Theory]
    [InlineData("", "col-b")]
    [InlineData("col-a", "")]
    public async Task Empty_collection_is_invalid_argument(string source, string dest)
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("t-1", (source, dest, "p1"))));
        Assert.True(host.HasPhoto(Source, "p1"));
    }

    [Fact]
    public async Task Missing_transfer_id_or_moves_is_invalid_argument()
    {
        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("")));
        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("t-1")));
    }

    [Fact]
    public async Task Duplicate_photo_is_invalid_argument()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("t-1", (Source, Dest, "p1"), (Source, "col-c", "p1"))));
        Assert.True(host.HasPhoto(Source, "p1"));
    }

    [Fact]
    public async Task Injected_failure_moves_nothing_and_a_retry_after_clearing_it_succeeds()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.WritePhoto("p2", Sidecar("Jay"), Source);
        host.TransferFailureStatusCode = StatusCode.Internal;

        Assert.Equal(StatusCode.Internal, await StatusOf(Request("t-1", (Source, Dest, "p1"), (Source, Dest, "p2"))));
        Assert.Equal(2, host.PhotoIds(Source).Count);
        Assert.Empty(host.PhotoIds(Dest));

        host.TransferFailureStatusCode = null;
        var response = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1"), (Source, Dest, "p2")));

        Assert.Equal(2, response.Results.Count);
        Assert.Empty(host.PhotoIds(Source));
        Assert.Equal(2, host.PhotoIds(Dest).Count);
    }

    [Fact]
    public async Task Repeating_a_completed_transfer_id_returns_original_mapping_and_moves_nothing_again()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        var first = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));
        var destIds = host.PhotoIds(Dest);

        var second = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));

        Assert.Equal(first.Results.Select(r => (r.OldPhotoId, r.NewPhotoId)), second.Results.Select(r => (r.OldPhotoId, r.NewPhotoId)));
        Assert.Equal(destIds, host.PhotoIds(Dest));
        Assert.Empty(host.PhotoIds(Source));
    }

    [Fact]
    public async Task Replay_succeeds_even_while_failure_is_injected()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        var first = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));
        host.TransferFailureStatusCode = StatusCode.Internal;

        var replay = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));

        Assert.Equal(first.Results[0].NewPhotoId, replay.Results[0].NewPhotoId);
    }

    [Fact]
    public async Task Different_transfer_ids_are_independent_and_calls_are_recorded()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.WritePhoto("p2", Sidecar("Jay"), Source);

        await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));
        await client.TransferPhotosAsync(Request("t-2", (Source, Dest, "p2")));

        Assert.Equal(2, host.PhotoIds(Dest).Count);
        Assert.Equal(["t-1", "t-2"], host.Transfers.Select(t => t.TransferId).ToArray());
    }

    [Fact]
    public async Task Moved_photo_can_be_moved_back_using_returned_mapping()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        var forward = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));

        var back = await client.TransferPhotosAsync(Request("t-1-undo", (Dest, Source, forward.Results[0].NewPhotoId)));

        Assert.Empty(host.PhotoIds(Dest));
        Assert.Equal(back.Results[0].NewPhotoId, Assert.Single(host.PhotoIds(Source)));
        var card = Assert.Single((await client.ListCardsAsync(new ListCardsRequest { CollectionId = Source })).Cards);
        Assert.Equal("Kai", card.CardName);
    }

    [Fact]
    public async Task Same_transfer_id_with_different_moves_is_already_exists_and_changes_nothing()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.WritePhoto("p2", Sidecar("Lloyd"), Source);
        await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));
        var destIds = host.PhotoIds(Dest);

        var status = await StatusOf(Request("t-1", (Source, Dest, "p2")));

        Assert.Equal(StatusCode.AlreadyExists, status);
        Assert.True(host.HasPhoto(Source, "p2"));
        Assert.Equal(destIds, host.PhotoIds(Dest));
    }

    [Theory]
    [InlineData("a/b", "col-b")]
    [InlineData("col-a", "TRANSFER#x")]
    [InlineData("col-a", "..")]
    public async Task Malformed_collection_id_is_invalid_argument_and_nothing_moves(string source, string dest)
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        Assert.Equal(StatusCode.InvalidArgument, await StatusOf(Request("t-1", (source, dest, "p1"))));
        Assert.True(host.HasPhoto(Source, "p1"));
        Assert.Empty(host.PhotoIds(Dest));
    }

    [Fact]
    public async Task Unknown_source_collection_is_not_found_and_nothing_moves()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);

        Assert.Equal(StatusCode.NotFound, await StatusOf(Request("t-1", ("unknown-col", Dest, "p1"))));
        Assert.True(host.HasPhoto(Source, "p1"));
        Assert.Empty(host.PhotoIds(Dest));
    }

    [Fact]
    public async Task Persistent_source_removal_failure_still_succeeds_and_a_replay_finishes_the_removal()
    {
        host.WritePhoto("p1", Sidecar("Kai"), Source);
        host.TransferSourceRemovalFails = true;

        var first = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));

        Assert.True(host.HasPhoto(Dest, first.Results[0].NewPhotoId));
        Assert.True(host.HasPhoto(Source, "p1"));

        host.TransferSourceRemovalFails = false;
        var replay = await client.TransferPhotosAsync(Request("t-1", (Source, Dest, "p1")));

        Assert.Equal(first.Results[0].NewPhotoId, replay.Results[0].NewPhotoId);
        Assert.False(host.HasPhoto(Source, "p1"));
        Assert.Single(host.PhotoIds(Dest));
    }
}
