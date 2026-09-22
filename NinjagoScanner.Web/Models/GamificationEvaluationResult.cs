namespace NinjagoScanner.Web.Models;

/// <summary>
/// The result of re-evaluating a collection's gamification state after an action that may have
/// changed Owned Copies or review confirmations (see GamificationService.EvaluateAsync). XpBefore
/// is supplied by the caller - captured via GamificationService.GetXpAsync() before the action ran
/// - so the rank-crossed comparison reflects the specific action, not just a point-in-time read.
/// </summary>
public sealed class GamificationEvaluationResult
{
    public required IReadOnlyList<Achievement> NewlyUnlockedAchievements { get; init; }
    public required int XpBefore { get; init; }
    public required int XpAfter { get; init; }
    public required RankDefinition RankBefore { get; init; }
    public required RankDefinition RankAfter { get; init; }

    public bool RankedUp => RankAfter.Level > RankBefore.Level;
    public int XpGained => XpAfter - XpBefore;
}
