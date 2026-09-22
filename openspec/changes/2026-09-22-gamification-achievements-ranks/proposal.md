## Why

The Web app tracks collection progress but gives no reward for it: scanning a card, confirming a review, or completing a series produces no visible acknowledgement. A gamification layer (achievements, XP, ranks, and an unlock moment at the point of action) has been prototyped and approved (see `design-reference/`). It reuses data the app already has - owned cards per series, rarity, duplicate counts, review confirmations - so most of it can be computed from existing sources.

## What Changes

- Add an **Achievements page** (`/achievements`) with: rank hero (rank badge, level, XP, progress to next rank), four stat cards, a "next up" strip of the three closest achievements, the full badge grid with tier/category/progress, a 10-step rank ladder, and the background-unlock shelf.
- Add an **achievement model + evaluation service**: each achievement has id, name, description, tier (bronze/silver/gold/legendary), category (Sammeln/Seltenheit/Aktivität/Tausch), a numeric goal, a current-progress rule, and an XP value. Progress is recomputed from the collection on change; unlock timestamps are persisted.
- Add an **XP + rank system**: 10 ranks (Novize, Schüler, Spinjitzu-Schüler, Funke, Sturm, Frost, Stein, Energie, Sensei, Goldener Ninja) with a rising curve (0 / 150 / 400 / 800 / 1500 / 2600 / 4200 / 6500 / 9800 / 14000 XP). XP sources: new card +50, confirmed review scan +10, achievement unlock = its own XP value. Duplicates award +10.
- Add an **unlock moment**: a full-screen overlay after a scan or review confirmation. Two card variants - "Neue Karte" (rarity-coloured rotating rays, card reveal, +50 XP) and "Dublette" (calmer, copy count, trade hint, +10 XP) - and a **rank-up variant** shown immediately after the card overlay is dismissed when the XP gain crossed a rank threshold.
- Add **achievement toasts**: staggered bottom-right toasts (kanji badge, name, XP) after the reveal, auto-dismissing after ~5.5s, so an unlock never competes with the card reveal.
- Surface the rank **outside** the achievements page: rank badge + XP in the header avatar, a rank/XP block on the overview/status page, and the rank name under each entry in the friends leaderboard.
- Add **rank-gated background unlocks** at ranks 4, 6, 8 and 10; the selected background applies to the app shell.

## Capabilities

### New Capabilities
- `web-achievements`: achievement definitions, progress evaluation, unlock persistence, and the achievements page.
- `web-rank-progression`: XP accrual, rank thresholds, rank display across the app, and rank-gated background unlocks.
- `web-unlock-feedback`: the post-scan / post-review celebration overlay (new card, duplicate, rank-up) and the achievement toast queue.

### Modified Capabilities
- `web-overview`: adds a rank & XP block above series progress; leaderboard rows show the rank name. No change to existing computed values.
- `web-photo-upload`: after a successful scan+match, raises the unlock-feedback event. No change to upload/analysis behavior.
- `web-card-review-flow`: "Confirm All" raises the unlock-feedback event for the confirmed group (new card vs duplicate). No change to review-status or grouping semantics.

## Impact

- Affected code (expected): new `NinjagoScanner.Web/Components/Pages/Achievements.razor`, new `Models/Achievement*.cs`, `Models/RankDefinition.cs`, a `Services/GamificationService.cs` (progress + XP + rank evaluation), new shared components for the unlock overlay and toast host in `MainLayout.razor`, additions to `Overview`/`Review`/`Upload` pages, and `app.css` additions for badge/tier/overlay styling.
- Persistence: unlock state, unlock timestamps, accrued XP and the selected background need a per-user store. The prototype assumes a single local user - decide between a JSON sidecar next to the collection data and a small table before implementing (see design.md, open question 1).
- No gRPC/proto changes. No changes to `CatalogService` or `PictureService`.
- Non-goals: friend-vs-friend XP comparison beyond the existing leaderboard, daily/weekly quests, trading, and prestige ranks past Goldener Ninja. These were discussed and deliberately deferred.
