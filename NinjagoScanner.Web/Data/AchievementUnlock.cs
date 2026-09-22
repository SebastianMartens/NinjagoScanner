namespace NinjagoScanner.Web.Data;

/// <summary>
/// Records the first time a collection's derived state crossed an achievement's goal - never the
/// achievement's current "is it unlocked" status, which is always recomputed live (see
/// Models/Achievement.cs). Composite key (CollectionId, AchievementId) makes a duplicate insert
/// for an already-crossed achievement a no-op rather than a second unlock.
/// </summary>
public class AchievementUnlock
{
    public string CollectionId { get; set; } = default!;
    public string AchievementId { get; set; } = default!;
    public DateTime UnlockedAtUtc { get; set; }
}
