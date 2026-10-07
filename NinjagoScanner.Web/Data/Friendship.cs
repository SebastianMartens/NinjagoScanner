namespace NinjagoScanner.Web.Data;

public enum FriendshipStatus
{
    Pending,
    Accepted
}

/// <summary>
/// One row per pair of users (pending request or accepted friendship). Declined, cancelled and
/// removed relationships delete the row. <see cref="UserLowId"/>/<see cref="UserHighId"/> hold the
/// two user IDs in canonical (ordinal) order so a unique index covers the unordered pair.
/// </summary>
public class Friendship
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string RequesterUserId { get; set; } = string.Empty;
    public string AddresseeUserId { get; set; } = string.Empty;
    public string UserLowId { get; set; } = string.Empty;
    public string UserHighId { get; set; } = string.Empty;
    public FriendshipStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }

    public static (string Low, string High) CanonicalPair(string userA, string userB) =>
        string.CompareOrdinal(userA, userB) <= 0 ? (userA, userB) : (userB, userA);
}
