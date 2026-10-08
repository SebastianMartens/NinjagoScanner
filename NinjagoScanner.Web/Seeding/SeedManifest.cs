using System.Text.Json;
using System.Text.Json.Serialization;
using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Seeding;

/// <summary>
/// Shape of <c>testdata/manifest.json</c>: the fixed users, collection IDs, friendship and pending
/// trade shared between the Web seed mode and the PictureService fixture seeding script, so both
/// stores agree on collection and photo IDs.
/// </summary>
public sealed class SeedManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Password { get; set; } = string.Empty;
    public List<SeedUser> Users { get; set; } = [];
    public SeedFriendship? Friendship { get; set; }
    public SeedTrade? PendingTrade { get; set; }

    public static SeedManifest Load(string path) =>
        JsonSerializer.Deserialize<SeedManifest>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"Seed manifest '{path}' is empty.");
}

public sealed class SeedUser
{
    public string UserName { get; set; } = string.Empty;
    public string CollectionId { get; set; } = string.Empty;
}

public sealed class SeedFriendship
{
    public string Requester { get; set; } = string.Empty;
    public string Addressee { get; set; } = string.Empty;
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Accepted;
}

public sealed class SeedTrade
{
    public string Id { get; set; } = string.Empty;
    public string Proposer { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public List<SeedTradeItem> Items { get; set; } = [];
}

public sealed class SeedTradeItem
{
    public TradeSide Side { get; set; }
    public string PhotoId { get; set; } = string.Empty;
    public string SeriesName { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string Rarity { get; set; } = string.Empty;
}
