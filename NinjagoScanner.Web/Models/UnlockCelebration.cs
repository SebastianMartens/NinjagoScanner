namespace NinjagoScanner.Web.Models;

public enum UnlockCelebrationKind
{
    NewCard,
    Duplicate,
    RankUp
}

/// <summary>The full-screen unlock-moment overlay's content (design.md "Unlock moment") - a card reveal (new/duplicate) or a rank-up that chains off one being dismissed.</summary>
public sealed class UnlockCelebration
{
    public required UnlockCelebrationKind Kind { get; init; }

    // Card variants (NewCard / Duplicate)
    public string? CardName { get; init; }
    public string? Series { get; init; }
    public string? CardNumber { get; init; }
    public string? Rarity { get; init; }
    public int Copies { get; init; }
    public int XpGained { get; init; }

    // Rank-up variant
    public RankDefinition? Rank { get; init; }
    public BackgroundUnlock? UnlockedBackground { get; init; }
}

/// <summary>One achievement-unlocked toast in the bottom-right queue.</summary>
public sealed class AchievementToast
{
    public required string ToastId { get; init; }
    public required Achievement Achievement { get; init; }
}
