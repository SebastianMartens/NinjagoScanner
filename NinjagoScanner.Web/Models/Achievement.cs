namespace NinjagoScanner.Web.Models;

public enum AchievementCategory
{
    Sammeln,
    Seltenheit,
    Aktivitaet
}

/// <summary>
/// A gamification achievement definition. <see cref="ProgressRule"/> and <see cref="GoalRule"/>
/// derive the current/goal values from a live <see cref="GamificationCollectionState"/> snapshot -
/// nothing about "is this unlocked" is ever stored, only the timestamp of the first time it was
/// (see <see cref="Data.AchievementUnlock"/>), so a later review correction that lowers the
/// collection's state is reflected immediately instead of leaving a stale unlock on display.
/// </summary>
public sealed class Achievement
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public required AchievementCategory Category { get; init; }
    public required int Xp { get; init; }
    public required Func<GamificationCollectionState, int> ProgressRule { get; init; }
    public Func<GamificationCollectionState, int> GoalRule { get; init; } = _ => 1;

    public static Achievement WithFixedGoal(
        string id, string name, string description, string glyph,
        AchievementCategory category, int goal, int xp,
        Func<GamificationCollectionState, int> progressRule)
    {
        return new Achievement
        {
            Id = id,
            Name = name,
            Description = description,
            Glyph = glyph,
            Category = category,
            Xp = xp,
            ProgressRule = progressRule,
            GoalRule = _ => goal
        };
    }
}

public static class AchievementCategoryLabels
{
    public static string ToGermanLabel(this AchievementCategory category) => category switch
    {
        AchievementCategory.Sammeln => "Sammeln",
        AchievementCategory.Seltenheit => "Seltenheit",
        AchievementCategory.Aktivitaet => "Aktivität",
        _ => category.ToString()
    };
}

/// <summary>
/// The initial set of 8 achievements (design.md "Initial set"). `team-ninja` and
/// `makelloser-scan` from the original 12-achievement prototype are deferred - see design.md
/// Deferred, same reasoning as the already-deferred streak achievement (no per-scan "was this
/// corrected" history exists to compute them from).
/// </summary>
public static class AchievementDefinitions
{
    public static readonly IReadOnlyList<Achievement> All = new[]
    {
        Achievement.WithFixedGoal(
            "first-scan", "Erster Fund", "Scanne deine allererste Karte.", "始",
            AchievementCategory.Aktivitaet, goal: 1, xp: 20,
            state => Math.Min(state.TotalPhotos, 1)),

        Achievement.WithFixedGoal(
            "ten-cards", "Zehnerpack", "10 verschiedene Karten im Besitz.", "拾",
            AchievementCategory.Sammeln, goal: 10, xp: 40,
            state => state.DistinctOwnedCards),

        new Achievement
        {
            Id = "every-series",
            Name = "Überall vertreten",
            Description = "Mindestens eine Karte in jeder Serie.",
            Glyph = "全",
            Category = AchievementCategory.Sammeln,
            Xp = 200,
            ProgressRule = state => state.SeriesWithAtLeastOneOwnedCard,
            GoalRule = state => state.TotalSeries
        },

        Achievement.WithFixedGoal(
            "series-complete", "Serie komplett", "Vervollständige eine ganze Serie.", "完",
            AchievementCategory.Sammeln, goal: 1, xp: 500,
            state => state.AnySeriesComplete ? 1 : 0),

        Achievement.WithFixedGoal(
            "limited-hunter", "Limit-Jäger", "Besitze 5 Limited-Edition-Karten.", "極",
            AchievementCategory.Seltenheit, goal: 5, xp: 90,
            state => state.LimitedEditionOwnedCards),

        Achievement.WithFixedGoal(
            "first-legendary", "Legendenbrecher", "Ziehe deine erste legendäre Karte.", "龍",
            AchievementCategory.Seltenheit, goal: 1, xp: 250,
            state => Math.Min(state.LegendaryOwnedCards, 1)),

        Achievement.WithFixedGoal(
            "review-50", "Prüfmeister", "Bestätige 50 Scans im Review.", "鑑",
            AchievementCategory.Aktivitaet, goal: 50, xp: 100,
            state => state.VerifiedReviewCount),

        Achievement.WithFixedGoal(
            "dupes-25", "Doppelt hält besser", "Sammle 25 Dubletten.", "双",
            AchievementCategory.Sammeln, goal: 25, xp: 50,
            state => state.DuplicateCopies),
    };
}

/// <summary>
/// The live, fully-derived inputs every achievement rule and the XP total are computed from -
/// see design.md's "Progress is derived, never stored" and the Persistence section.
/// </summary>
public sealed class GamificationCollectionState
{
    public required int TotalPhotos { get; init; }
    public required int DistinctOwnedCards { get; init; }
    public required int DuplicateCopies { get; init; }
    public required int SeriesWithAtLeastOneOwnedCard { get; init; }
    public required int TotalSeries { get; init; }
    public required bool AnySeriesComplete { get; init; }
    public required int VerifiedReviewCount { get; init; }
    public required int LegendaryOwnedCards { get; init; }
    public required int LimitedEditionOwnedCards { get; init; }
}

/// <summary>One achievement's live progress, for the Achievements page.</summary>
public sealed class AchievementProgress
{
    public required Achievement Achievement { get; init; }
    public required int Current { get; init; }
    public required int Goal { get; init; }
    public required bool IsUnlocked { get; init; }
    public required DateTime? UnlockedAtUtc { get; init; }

    public int PercentComplete => Goal <= 0 ? 100 : Math.Min(100, (int)Math.Round(100.0 * Current / Goal));
}
