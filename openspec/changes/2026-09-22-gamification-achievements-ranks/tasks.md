# Tasks: Gamification (Achievements, XP, Ranks, Unlock Moments)

## 1. Decisions before code
- [ ] Pick the persistence store for unlock timestamps / selected background (design.md Q1).
- [ ] Confirm whether per-scan timestamps exist for `streak-7` (Q2); if not, mark it deferred.
- [ ] Decide hide-vs-show for the Tausch category (Q3).

## 2. Model + service
- [ ] `Models/Achievement.cs`, `AchievementTier`, `AchievementCategory`, `UnlockedAchievement`.
- [ ] `Models/RankDefinition.cs` + the 10-rank table from design.md.
- [ ] `Services/GamificationService.cs`: `GetProgress()` (all achievements with current/goal), `GetXp()` (derived total), `GetRank(xp)`, `EvaluateAfter(scanResult)` returning newly unlocked achievements + whether a rank was crossed.
- [ ] Persist unlock timestamps; never persist derived progress.
- [ ] Unit tests: each achievement rule, the XP total, rank boundaries (exact threshold = new rank), and "review correction removes a card" → XP and unlocks recompute downward.

## 3. Achievements page
- [ ] `Components/Pages/Achievements.razor` at `/achievements`, added to nav (desktop + mobile tab bar).
- [ ] Rank hero: badge, "Rang N", rank name, XP, next-rank label, progress bar, XP-source caption.
- [ ] Four stat cards: unlocked/total, percent, XP, legendary badges.
- [ ] "Fast geschafft": three closest incomplete achievements by percent.
- [ ] Badge grid with filter (Alle / Freigeschaltet / In Arbeit / Gesperrt), tier chip, category chip, progress bar, unlock date.
- [ ] Rank ladder: all 10, horizontally scrollable, reached ones highlighted, current one outlined.
- [ ] Background shelf: 4 tiles, locked ones greyscaled with "Ab Rang N".
- [ ] Empty state when a filter matches nothing.

## 4. Unlock feedback
- [ ] Shared `UnlockOverlay` component (new card / duplicate / rank-up variants) hosted in `MainLayout.razor`.
- [ ] Shared `ToastHost` component with staggered entry and auto-dismiss.
- [ ] Event/queue so Upload and Review can raise a celebration without knowing about the overlay.
- [ ] Panel swallows clicks; backdrop scrolls; card/badge sized with `clamp` against viewport height.
- [ ] Rank-up overlay chains off the card overlay's dismissal, exactly once.

## 5. Rank visibility
- [ ] Header: rank kanji badge + name + XP (text hidden below 780px).
- [ ] Status page: "Rang & Erfahrung" block above series progress.
- [ ] Leaderboard rows: rank name caption.

## 6. Hook-ups
- [ ] Upload: raise celebration after a successful scan+match (new vs duplicate from the collection).
- [ ] Review: raise celebration on "Confirm All" per group.
- [ ] Ensure a corrected review re-evaluates and does not double-award.

## 7. Styling
- [ ] Tier accent tokens, badge card states, overlay + toast styles in `app.css`.
- [ ] Keyframes `cv-reveal`, `cv-ray`, `cv-toast-in`.
- [ ] Reduced-motion: rays and overshoot disabled under `prefers-reduced-motion`.

## 8. Assets
- [ ] Reuse existing background art for ranks 4/6/8.
- [ ] Commission or generate art for "Goldener Tresor" (rank 10) - gradient placeholder until then.

## 9. Verification
- [ ] Achievements page renders at 360px, 780px and 1440px without horizontal overflow (ladder scrolls).
- [ ] Overlay CTA fully visible at 540px viewport height.
- [ ] Rank-up reachable via the primary button, not only via backdrop click.
- [ ] Unlocking an achievement twice is impossible; timestamps survive restart.
- [ ] Contrast: tier-coloured text on badge cards ≥ 4.5:1.
- [ ] Archive the change once verified.
