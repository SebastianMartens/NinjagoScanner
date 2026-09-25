using NinjagoScanner.Web.Models;

namespace NinjagoScanner.Web.Services;

/// <summary>
/// The queue Upload and Review raise a celebration into without knowing anything about the
/// overlay/toast components that render it (web-unlock-feedback) - see design.md's "Unlock
/// moment". Scoped (one instance per Blazor circuit) so MainLayout's UnlockOverlay/ToastHost, and
/// whichever page just acted, share the same queue.
/// </summary>
internal sealed class GamificationCelebrationCenter : IDisposable
{
    private const int ToastStaggerMs = 700;
    private const int FirstToastDelayAfterOverlayMs = 1100;
    private const int ToastLifetimeMs = 5600;

    private readonly CancellationTokenSource disposedCts = new();
    private readonly List<AchievementToast> toasts = [];
    private RankDefinition? pendingRankUp;

    /// <summary>Raised whenever <see cref="Current"/> or <see cref="Toasts"/> changes, including from a background toast timer - subscribers must marshal back via InvokeAsync themselves.</summary>
    public event Action? Changed;

    public UnlockCelebration? Current { get; private set; }

    public IReadOnlyList<AchievementToast> Toasts => toasts;

    /// <summary>A scan or review correction that changed Owned Copies: always shows the card overlay. Achievement toasts queue staggered, starting ~1.1s after the overlay opens.</summary>
    public void CelebrateCard(
        UnlockCelebrationKind kind,
        string cardName,
        string series,
        string cardNumber,
        string? rarity,
        int copies,
        GamificationEvaluationResult evaluation)
    {
        Current = new UnlockCelebration
        {
            Kind = kind,
            CardName = cardName,
            Series = series,
            CardNumber = cardNumber,
            Rarity = rarity,
            Copies = copies,
            XpGained = kind == UnlockCelebrationKind.NewCard ? GamificationService.XpNewCard : 0
        };

        if (evaluation.RankedUp)
        {
            pendingRankUp = evaluation.RankAfter;
        }

        QueueToasts(evaluation.NewlyUnlockedAchievements, initialDelayMs: FirstToastDelayAfterOverlayMs);
        Changed?.Invoke();
    }

    /// <summary>
    /// A routine action with no Owned-Copies change (e.g. "Confirm All" on an already-correct
    /// match): no overlay, but any achievement it unlocked still toasts immediately, and a rank
    /// crossed purely from it is deferred to the next event that shows a card overlay.
    /// </summary>
    public void CelebrateSilently(GamificationEvaluationResult evaluation)
    {
        if (evaluation.RankedUp)
        {
            pendingRankUp = evaluation.RankAfter;
        }

        QueueToasts(evaluation.NewlyUnlockedAchievements, initialDelayMs: 0);
        Changed?.Invoke();
    }

    /// <summary>Dismisses the current overlay (backdrop click or "Weiter"). If a rank was crossed and not yet celebrated, the rank-up overlay replaces it exactly once instead of closing.</summary>
    public void DismissCurrent()
    {
        if (pendingRankUp is { } rank)
        {
            pendingRankUp = null;
            var background = BackgroundUnlocks.All.FirstOrDefault(unlock => unlock.RequiredRankLevel == rank.Level);
            Current = new UnlockCelebration { Kind = UnlockCelebrationKind.RankUp, Rank = rank, UnlockedBackground = background };
        }
        else
        {
            Current = null;
        }

        Changed?.Invoke();
    }

    public void DismissToast(string toastId)
    {
        if (toasts.RemoveAll(toast => toast.ToastId == toastId) > 0)
        {
            Changed?.Invoke();
        }
    }

    private void QueueToasts(IReadOnlyList<Achievement> achievements, int initialDelayMs)
    {
        for (var index = 0; index < achievements.Count; index++)
        {
            var toast = new AchievementToast { ToastId = $"{achievements[index].Id}-{Guid.NewGuid():n}", Achievement = achievements[index] };
            _ = ShowAfterDelayAsync(toast, initialDelayMs + index * ToastStaggerMs);
        }
    }

    private async Task ShowAfterDelayAsync(AchievementToast toast, int delayMs)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, disposedCts.Token);
            }

            toasts.Add(toast);
            Changed?.Invoke();

            await Task.Delay(ToastLifetimeMs, disposedCts.Token);
            toasts.Remove(toast);
            Changed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Circuit gone - nothing left to show.
        }
    }

    public void Dispose()
    {
        disposedCts.Cancel();
        disposedCts.Dispose();
    }
}
