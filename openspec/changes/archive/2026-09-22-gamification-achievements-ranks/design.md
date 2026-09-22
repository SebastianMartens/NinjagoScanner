# Design: Gamification (Achievements, XP, Ranks, Unlock Moments)

## Visual reference

`design-reference/Ninjago Card Vault.dc.html` - open in a browser, navigate to **Erfolge**. Also relevant: header avatar (rank badge), **Status** page (rank & XP block, leaderboard rank names), **Upload** → "Take Photo" and **Review** → "Confirm All" (unlock overlays; three demo pulls cycle through new card / duplicate / new card + rank-up).

The reference is a prototype: its data is hard-coded and its XP baseline is seeded (`DEMO_XP_OFFSET`) so the rank-up is reachable in two clicks. Recreate the **visuals and interaction**, not the demo data.

## Achievement model

```
Achievement
  Id            string        // stable, used for persistence
  Name          string
  Description   string
  Glyph         string        // single kanji, chosen by meaning (see table)
  Category      Sammeln | Seltenheit | Aktivität
  Goal          int
  Xp            int
  ProgressRule  func(CollectionState) -> int   // current value, clamped at Goal
```

Persisted per user: `{ achievementId, unlockedAtUtc }`. Progress itself is **derived**, never stored - recompute on collection change so a corrected review can't leave a stale unlock.

### Initial set (8)

| Id | Glyph | Name | Category | Rule | XP |
|---|---|---|---|---|---|
| first-scan | 始 | Erster Fund | Aktivität | first card scanned | 20 |
| ten-cards | 拾 | Zehnerpack | Sammeln | 10 distinct cards owned | 40 |
| every-series | 全 | Überall vertreten | Sammeln | ≥1 card in each of the 16 series | 200 |
| series-complete | 完 | Serie komplett | Sammeln | any series at 100% | 500 |
| limited-hunter | 極 | Limit-Jäger | Seltenheit | 5 Limited Edition cards owned | 90 |
| first-legendary | 龍 | Legendenbrecher | Seltenheit | 1 Legendary owned | 250 |
| review-50 | 鑑 | Prüfmeister | Aktivität | 50 scans confirmed in review | 100 |
| dupes-25 | 双 | Doppelt hält besser | Sammeln | 25 duplicate copies | 50 |

## Ranks

10 ranks, rising curve, designed as a one-year goal for an active collector:

| # | Name | Glyph | XP | Accent |
|---|---|---|---|---|
| 1 | Novize | 初 | 0 | neutral |
| 2 | Schüler | 学 | 150 | blue-grey |
| 3 | Spinjitzu-Schüler | 旋 | 400 | blue |
| 4 | Funke | 火 | 800 | fire orange |
| 5 | Sturm | 雷 | 1500 | lightning yellow |
| 6 | Frost | 氷 | 2600 | ice blue |
| 7 | Stein | 土 | 4200 | earth green |
| 8 | Energie | 気 | 6500 | purple |
| 9 | Sensei | 師 | 9800 | magenta |
| 10 | Goldener Ninja | 金 | 14000 | gold |

XP: achievement unlock = its own XP. XP is a **derived total** (recompute from unlocked achievements) rather than an incrementing counter - that keeps it correct when a review is later corrected. Store only what can't be derived (e.g. historical trade XP once trading exists).

Background unlocks: rank 4 → Elementarnebel, 6 → Nebelgipfel, 8 → Energie-Dojo, 10 → Goldener Tresor. The last one has no art yet (gradient placeholder in the prototype).

## Unlock moment

**Trigger**: only events that change **Owned Copies** (`SeriesName`+`CardNumber` newly matching a catalog card) - a scan+match on Upload, or a Review correction that changes which card a photo matches. Routine "Confirm All" on an already-correct match only changes **Review Status**, never **Owned Copies** (they're independent per the glossary), so it raises no overlay and no toast - it's the common-case action and would otherwise dilute every real reveal. Achievement progress (e.g. `review-50`, which counts confirmations) is still re-evaluated on every review action regardless of overlay - see toast note below.

Sequence after a triggering event:

1. **Card overlay** (fixed, z 60, blurred backdrop, backdrop click or "Weiter" dismisses).
   - *New card*: rotating conic rays in rarity colour, card reveal with overshoot (`cv-reveal`), name, series + number, rarity chip, "Zu deiner Sammlung hinzugefügt".
   - *Duplicate*: no rays, neutral accent, "Du besitzt diese Karte jetzt N×".
2. **Achievement toasts** queue in bottom-right, staggered ~700ms apart, starting ~1.1s after the overlay opens; auto-dismiss ~5.6s; click to dismiss. Toasts are evaluated independently of the card overlay: an action with no ownership change (e.g. a routine "Confirm All" that crosses `review-50`) shows no overlay but still queues its toast immediately, without the ~1.1s offset.
3. **Rank-up overlay** replaces the card overlay on dismissal *if* the XP gain crossed a threshold: rank kanji badge, rank name, "Rang N erreicht", and the unlocked background as the stated reward. Since a routine "Confirm All" shows no card overlay, a rank crossed purely from accrued review XP has no overlay to chain off - defer that rank-up overlay to the next triggering event (scan or matching correction) that does show one.

Implementation notes carried over from prototype bugs worth not repeating:
- The panel must swallow clicks (`stopPropagation`), otherwise the primary button's click also hits the backdrop and the rank-up step is silently consumed.
- The backdrop needs `overflow:auto` and the panel `max-height:100%`; card art and rank badge use `clamp(...)` against viewport height so the CTA stays visible at ~540px viewport height.

## Rank visibility outside the achievements page

- **Header**: rank name + XP (right-aligned, small) next to a 34px circular kanji badge in the rank's accent colour. Replaces the empty avatar circle. On mobile the text hides, badge stays.
- **Status page**: a "Rang & Erfahrung" block above series progress - badge, "Rang N · Name", "X XP · noch Y XP bis Z", and the same gradient progress bar as series rows.
- **Leaderboard**: Leaderboard is not yet implemented. Later: rank name as a small uppercase caption under each player name. Sorting stays by owned cards for now; switching it to XP is a separate decision.

## Styling

Reuse the tokens from the visual-refresh change. New additions:
- Unlocked badge cards use a 160° gradient from `accent / 0.14` into the elevated surface with an `accent / 0.5` border, `accent` a single fixed badge colour (no per-achievement variation); locked cards are flat `oklch(18% 0.018 290)` with muted text and a dimmed badge.
- New keyframes: `cv-reveal` (scale overshoot), `cv-ray` (16s linear rotation), `cv-toast-in` (slide from right).
- Kanji render in Noto Sans JP, already loaded by the visual-refresh change.

## Persistence

Achievements, XP, and rank are scoped **per Collection**, not per User — consistent with `ProgressRule(CollectionState) -> int` above: all members of a collection (Owner and Reader, via `CollectionMembership`) see and share the same unlocks and rank, matching how `CollectionQueryService` already computes everything off the collection's photos rather than per-member.

Storage: two new tables in Web's existing SQLite database (`NinjagoScanner.Web/Data/AppDbContext.cs`), the same store that already backs ASP.NET Core Identity and `CollectionMembership`, on the volume already mounted in `NinjagoScanner.Web/fly.toml`. No new infra, no gRPC changes, no AWS access added to Web (AWS credentials stay PictureService-only per `infra/README.md`).

```csharp
public class AchievementUnlock
{
    public string CollectionId { get; set; } = default!;   // FK -> Collection
    public string AchievementId { get; set; } = default!;  // matches Achievement.Id above
    public DateTime UnlockedAtUtc { get; set; }
    // composite key: (CollectionId, AchievementId)
}

public class GamificationProfile
{
    public string CollectionId { get; set; } = default!;   // PK, FK -> Collection
    public int BonusXp { get; set; }                        // non-derivable XP (future trade XP etc.), default 0
    public string? SelectedBackgroundId { get; set; }
}
```

Rejected alternatives:
- **DynamoDB sidecar table** (PictureService) — keyed `CollectionId`+`PhotoId`, scoped to photo sidecars; gamification state isn't per-photo, and Web has no AWS access today, so this would mean routing every unlock through a new PictureService gRPC surface for data that has nothing to do with photos.
- **JSON sidecar file** — doesn't correspond to any existing pattern; nothing in this system persists runtime state to local JSON today (`cardInfos/*.json` is static build-time reference data), and collection data itself lives in S3/DynamoDB, not on disk.

## Deferred

- **Streak achievement**: no per-scan timestamp log exists today to compute a streak from. A `streak-*` achievement is out of scope for this change; revisit once/if such a log exists.
- **"Makelloser Scan" achievement** (prototype: 20 scans accepted without correction): same blocker as the streak achievement - nothing tracks whether a given scan was later corrected, so "accepted without correction" isn't computable today. Revisit alongside the streak achievement if that history ever gets tracked.
- **"Team Ninja" achievement** (prototype: own all six main ninjas): would require hardcoding which six card names/numbers count as "the six main ninja" across all 16 series, a content decision not covered by this change's scope. Revisit as a deliberate content-curation task if wanted.
- **Leaderboard**: not yet implemented (per Rank visibility above), so surfacing rank there is out of scope for this change. Revisit rank display and XP-vs-owned-cards sorting once a leaderboard exists.
- **Trade / Tausch category**: card sharing isn't implemented, so the Tausch category is dropped from the initial category enum rather than shipped empty (`dupes-25` moved to Sammeln - it only counts owned duplicates, not actual trades). The "bereit zum Tauschen" duplicate-overlay copy is dropped too, since it implied a trade action that doesn't exist. Revisit once card sharing ships.
