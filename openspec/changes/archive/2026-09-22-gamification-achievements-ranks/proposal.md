## Why

The Web app tracks collection progress but gives no reward for it: scanning a card, confirming a review, or completing a series produces no visible acknowledgement. A gamification layer (achievements, XP, ranks, and an unlock moment at the point of action) has been prototyped and approved (see `design-reference/`). It reuses data the app already has - owned cards per series, rarity, duplicate counts, review confirmations - so most of it can be computed from existing sources.

## What Changes

- Add an **Achievements page** (`/achievements`) with: rank hero (rank badge, level, XP, progress to next rank), four stat cards, a "next up" strip of the three closest achievements, the full badge grid with category/progress, a 10-step rank ladder, and the background-unlock shelf.
- Add an **achievement model + evaluation service**: each achievement has id, name, description, category (Sammeln/Seltenheit/Aktivität), a numeric goal, a current-progress rule, and an XP value. Progress is recomputed from the collection on change; unlock timestamps are persisted.
- Add an **XP + rank system**: 10 ranks (Novize, Schüler, Spinjitzu-Schüler, Funke, Sturm, Frost, Stein, Energie, Sensei, Goldener Ninja) with a rising curve (0 / 150 / 400 / 800 / 1500 / 2600 / 4200 / 6500 / 9800 / 14000 XP). XP sources: new card +50, duplicate copy +10, confirmed review scan +10 (accrues silently, no celebration - see unlock moment below), achievement unlock = its own XP value.
- Add an **unlock moment**: a full-screen overlay after a scan, or a review correction that changes which catalog card a photo matches - both are the only events that change Owned Copies. Two card variants - "Neue Karte" (rarity-coloured rotating rays, card reveal, +50 XP) and "Dublette" (calmer, copy count, +10 XP) - and a **rank-up variant** shown immediately after the card overlay is dismissed when the XP gain crossed a rank threshold. A routine "Confirm All" on an already-correct match changes only Review Status, never Owned Copies, so it raises no overlay - that's the common-case review action and would otherwise dilute every real reveal.
- Add **achievement toasts**: staggered bottom-right toasts (kanji badge, name, XP), auto-dismissing after ~5.5s. Timed after the card reveal when one is showing; fire immediately on their own when an achievement unlocks from an action with no overlay (e.g. crossing `review-50` via a routine confirmation).
- Surface the rank **outside** the achievements page: rank badge + XP in the header avatar, and a rank/XP block on the overview/status page. (The leaderboard doesn't exist yet - out of scope for this change; see design.md Deferred.)
- Add **rank-gated background unlocks** at ranks 4, 6, 8 and 10; the selected background applies to the app shell.

## Capabilities

### New Capabilities
- `web-achievements`: achievement definitions, progress evaluation, unlock persistence, and the achievements page.
- `web-rank-progression`: XP accrual, rank thresholds, rank display across the app, and rank-gated background unlocks.
- `web-unlock-feedback`: the celebration overlay (new card, duplicate, rank-up) shown only for events that change Owned Copies, and the achievement toast queue, which can fire standalone without an overlay.

### Modified Capabilities
- `web-overview`: adds a rank & XP block above series progress. No change to existing computed values.
- `web-photo-upload`: after a successful scan+match, raises the unlock-feedback event. No change to upload/analysis behavior.
- `web-card-review-flow`: a correction that changes a photo's matched card (SeriesName/CardNumber) raises the unlock-feedback event (new card vs duplicate), since that's an Owned Copies change. Routine "Confirm All" on an already-correct match raises no unlock-feedback event - only achievement/XP re-evaluation (e.g. `review-50`), surfaced as a standalone toast if it unlocks something. No change to review-status or grouping semantics.

## Impact

- Affected code (expected): new `NinjagoScanner.Web/Components/Pages/Achievements.razor`, new `Models/Achievement*.cs`, `Models/RankDefinition.cs`, a `Services/GamificationService.cs` (progress + XP + rank evaluation), new shared components for the unlock overlay and toast host in `MainLayout.razor`, additions to `Overview`/`Review`/`Upload` pages, `app.css` additions for badge/overlay styling, and two new `AppDbContext` tables (`AchievementUnlock`, `GamificationProfile`) plus an EF Core migration.
- Persistence: unlock timestamps, accrued non-derivable XP, and the selected background are scoped **per Collection** (shared across Owner/Reader members) and stored in Web's existing SQLite database - the same store already backing ASP.NET Core Identity and `CollectionMembership`, on the volume already mounted in `fly.toml`. No new infra; see design.md's Persistence section.
- No gRPC/proto changes. No changes to `CatalogService` or `PictureService`.
- Non-goals: friend-vs-friend XP comparison beyond the existing leaderboard, daily/weekly quests, trading, and prestige ranks past Goldener Ninja. These were discussed and deliberately deferred.
