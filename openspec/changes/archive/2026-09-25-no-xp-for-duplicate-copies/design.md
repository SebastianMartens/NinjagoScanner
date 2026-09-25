## Context

XP is a **derived total**, recomputed on every read in `GamificationService.ComputeXp` from the collection's catalog and photo state (see `archive/2026-09-22-gamification-achievements-ranks/design.md`). Today the formula includes `DuplicateCopies * XpDuplicateCopy` (10). Only achievement unlock timestamps and `BonusXp` are persisted.

Unlock feedback is raised in two places:
- `Upload.razor` → `TryCelebrateMatchAsync` picks `NewCard` or `Duplicate` from the matched card's `OwnedCopies` and calls `CelebrationCenter.CelebrateCard`.
- `Review.razor` → `TryCelebrateCardChangeAsync` does the same for corrections that change the ownership key, counting owned copies from the in-memory `ReviewSnapshot`. When the key didn't change, it falls back to `CelebrateSilentlyIfAnythingUnlockedAsync`, which toasts only newly unlocked achievements or a rank-up.

`GamificationCelebrationCenter.CelebrateCard` sets `XpGained` to `XpDuplicateCopy` for the `Duplicate` kind, and `UnlockOverlay.razor` always renders `+{XpGained} XP` for card variants.

## Goals / Non-Goals

**Goals:**
- Remove duplicate copies from the XP formula in one place, so upload, review, header, overview, and `/achievements` all agree.
- Make a review correction to an already-owned card behave like a routine "Confirm All": achievement toasts only.

**Non-Goals:**
- Distinguishing duplicates by source (upload vs. review). With a derived XP model that would mean persisting per-event XP, which isn't worth it for this.
- Changing the `dupes-25` achievement, its goal, or its XP.
- Any "rank-down" handling or notification for collections whose XP shrinks.

## Decisions

**Remove the duplicate term from the XP formula instead of setting the constant to 0.** Delete `XpDuplicateCopy` and its `ComputeXp` term. Keep `GamificationCollectionState.DuplicateCopies`, because `dupes-25` reads it. A zero-valued constant would leave dead code and still invite a "+0 XP" rendering path.

**Review: route duplicate matches through the existing silent path.** In `TryCelebrateCardChangeAsync`, when `ownedCopies > 1` after the correction, call `CelebrateSilentlyIfAnythingUnlockedAsync(xpBefore)` and return. This reuses the exact behavior the spec asks for (toasts for newly unlocked achievements only) and needs no new celebration-center API. The alternative was a new "quiet duplicate" kind in `GamificationCelebrationCenter`. It was rejected: the overlay-vs-toast split already exists in the silent path.

*Rank-up on this path:* `CelebrateSilently` also fires on `RankedUp`. With duplicate XP gone, a duplicate correction can only rank up through an achievement it unlocks (e.g. `dupes-25`'s 50 XP). In that case a notification is legitimate, and it matches how "Confirm All" behaves. It isn't "XP for a duplicate".

**Upload: keep the `Duplicate` overlay but show no XP.** `CelebrateCard` sets `XpGained = 0` for `Duplicate`. `UnlockOverlay.XpNote` returns nothing for a card variant with `XpGained == 0`, and the `.unlock-xp-note` element is omitted instead of rendering empty. The user decided upload keeps its Dublette reveal. Only the XP claim is wrong now.

## Risks / Trade-offs

- [Existing collections with duplicates lose 10 XP per duplicate on the next read and may drop a rank] → Acceptable and intended. XP is derived, and nothing is persisted that needs migrating. Persisted achievement unlocks are untouched, and no rank-down notification exists to fire. Mention it in the release notes/commit.
- [The "+10 XP" duplicate copy may be hard-coded in other UI copy (e.g. the `/achievements` XP-sources caption)] → That caption currently lists only new card, confirmed scan, and achievements, so it's already correct. The tasks include a grep for stray `XpDuplicateCopy`/"Dublette … XP" references.
