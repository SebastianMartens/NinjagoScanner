namespace NinjagoScanner.Web.Data;

/// <summary>
/// Per-collection gamification state that can't be derived from the collection itself.
/// <see cref="BonusXp"/> is reserved for future non-derivable XP sources (e.g. trade XP once
/// trading exists); everything else (achievement XP, card/duplicate/review XP) is computed live
/// from the collection on every read, never stored here.
/// </summary>
public class GamificationProfile
{
    public string CollectionId { get; set; } = default!;
    public int BonusXp { get; set; }
    public string? SelectedBackgroundId { get; set; }
}
