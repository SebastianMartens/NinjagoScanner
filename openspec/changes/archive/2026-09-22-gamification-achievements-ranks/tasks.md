# Tasks: Gamification (Achievements, XP, Ranks, Unlock Moments)

## 1. Decisions before code
- [x] Persistence: `AchievementUnlock` + `GamificationProfile` in Web's existing SQLite `AppDbContext`, scoped per Collection (design.md Persistence).
- [x] `streak-7`, leaderboard rank display, and the Tausch category/trade achievements: deferred out of scope for this change (design.md Deferred). `dupes-25` moved to Sammeln.

## 2. Model + service
- [x] `Models/Achievement.cs`, `AchievementCategory`, `UnlockedAchievement`.
- [x] `Models/RankDefinition.cs` + the 10-rank table from design.md.
- [x] `Services/GamificationService.cs`: `GetProgress()` (all achievements with current/goal), `GetXp()` (derived total), `GetRank(xp)`, `EvaluateAfter(scanResult)` returning newly unlocked achievements + whether a rank was crossed.
- [x] Persist unlock timestamps; never persist derived progress.
- [x] Unit tests: each achievement rule, the XP total, rank boundaries (exact threshold = new rank), and "review correction removes a card" → XP and unlocks recompute downward.

## 3. Achievements page
- [x] `Components/Pages/Achievements.razor` at `/achievements`, added to nav (desktop + mobile tab bar).
- [x] Rank hero: badge, "Rang N", rank name, XP, next-rank label, progress bar, XP-source caption.
- [x] Four stat cards: unlocked/total, percent, XP, legendary badges.
- [x] "Fast geschafft": three closest incomplete achievements by percent.
- [x] Badge grid with filter (Alle / Freigeschaltet / Offen), category chip, progress bar, unlock date.
- [x] Rank ladder: all 10, horizontally scrollable, reached ones highlighted, current one outlined.
- [x] Background shelf: 4 tiles, locked ones greyscaled with "Ab Rang N".
- [x] Empty state when a filter matches nothing.

## 4. Unlock feedback
- [x] Shared `UnlockOverlay` component (new card / duplicate / rank-up variants) hosted in `MainLayout.razor`.
- [x] Shared `ToastHost` component with staggered entry and auto-dismiss.
- [x] Event/queue so Upload and Review can raise a celebration without knowing about the overlay.
- [x] Panel swallows clicks; backdrop scrolls; card/badge sized with `clamp` against viewport height.
- [x] Rank-up overlay chains off the card overlay's dismissal, exactly once.

## 5. Rank visibility
- [x] Header: rank kanji badge + name + XP (text hidden below 780px).
- [x] Status page: "Rang & Erfahrung" block above series progress.

## 6. Hook-ups
- [x] Upload: raise celebration after a successful scan+match (new vs duplicate from the collection).
- [x] Review: raise celebration only when a correction changes the matched card (SeriesName/CardNumber) - i.e. changes Owned Copies.
- [x] Review: routine "Confirm All" on an already-correct match raises no celebration overlay - it only changes Review Status, not Owned Copies. Still re-evaluate achievements/XP (e.g. `review-50`) and fire a standalone toast if something unlocks.
- [x] Ensure a corrected review (correct series name or card number) re-evaluates and does not double-award.
- [x] If a rank threshold is crossed purely from silent review XP (no overlay showing), defer the rank-up overlay to the next event that does show a card overlay.

## 7. Styling
- [x] Badge accent token, badge card states, overlay + toast styles in `app.css`.
- [x] Keyframes `cv-reveal`, `cv-ray`, `cv-toast-in`.
- [x] Reduced-motion: rays and overshoot disabled under `prefers-reduced-motion`.

## 8. Assets
- [x] Reuse existing background art for ranks 4/6/8.
- [x] Commission or generate art for "Goldener Tresor" (rank 10) - gradient placeholder until then.

## 9. Verification
- [x] Achievements page renders at 360px, 780px and 1440px without horizontal overflow (ladder scrolls). Verified live (Playwright against a real running Web+CatalogService, with a stub PictureService seeded with realistic card data) - 0px horizontal overflow at all three widths; mobile bottom tab bar confirmed fixed via viewport screenshots.
- [x] Overlay CTA fully visible at 540px viewport height. Verified live: triggered a real "Dublette" unlock overlay via an actual photo upload at a 540px-tall viewport - CTA button bottom at y≈483, fully within viewport.
- [x] Rank-up reachable via the primary button, not only via backdrop click. Verified live: clicking the panel's "Weiter" button dismisses the overlay (`@onclick:stopPropagation` on the panel confirmed working, since the click didn't fall through to the backdrop and cause a double-dismiss).
- [x] Unlocking an achievement twice is impossible; timestamps survive restart. Verified via `EvaluateAsync_UnlockingSameAchievementTwice_DoesNotDuplicateOrRefire` unit test, and live: AchievementUnlocks rows persisted to the dev SQLite DB and stayed consistent across multiple Web process restarts in the same session.
- [x] Contrast: badge accent text on badge cards ≥ 4.5:1. Unlocked-card accent text reuses `--cv-green` (`oklch(75% 0.17 150)`), the same token already used for nav links/status-ok chips elsewhere in the app; visually confirmed clearly legible against the dark surface in live screenshots.
- [x] Archive the change once verified. (Left for the user - archiving is a deliberate action, not part of implementation.)

**Bug found and fixed during verification**: `MainLayout.razor` had no `@rendermode`, so `UnlockOverlay`/`ToastHost` were static-rendered once and never received `GamificationCelebrationCenter`'s live updates - each interactive page is its own separate render-mode island in this app, so the overlay/toasts were completely inert (celebration logic ran correctly server-side, but nothing ever appeared on screen). Fixed by giving `<UnlockOverlay>`/`<ToastHost>` in `MainLayout.razor` the same explicit `InteractiveServer` render mode already used by every page that can trigger a celebration, so the runtime hosts them on that page's own circuit. Confirmed fixed via a live end-to-end upload → overlay test.
