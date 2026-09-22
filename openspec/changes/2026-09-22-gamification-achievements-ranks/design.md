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
  Tier          bronze | silver | gold | legendary
  Category      Sammeln | Seltenheit | Aktivität | Tausch
  Goal          int
  Xp            int
  ProgressRule  func(CollectionState) -> int   // current value, clamped at Goal
```

Persisted per user: `{ achievementId, unlockedAtUtc }`. Progress itself is **derived**, never stored - recompute on collection change so a corrected review can't leave a stale unlock.

### Initial set (12)

| Id | Glyph | Name | Rule | Tier | XP |
|---|---|---|---|---|---|
| first-scan | 始 | Erster Fund | first card scanned | bronze | 20 |
| ten-cards | 拾 | Zehnerpack | 10 distinct cards owned | bronze | 40 |
| every-series | 全 | Überall vertreten | ≥1 card in each of the 16 series | gold | 200 |
| series-complete | 完 | Serie komplett | any series at 100% | gold | 180 |
| ultra-hunter | 極 | Ultra-Jäger | 5 Ultra Rare owned | silver | 90 |
| first-legendary | 龍 | Legendenbrecher | 1 Legendary owned | legendary | 250 |
| streak-7 | 連 | Sieben Tage Spinjitzu | scans on 7 consecutive days | silver | 80 |
| review-50 | 鑑 | Prüfmeister | 50 scans confirmed in review | silver | 100 |
| dupes-25 | 双 | Doppelt hält besser | 25 duplicate copies | bronze | 50 |
| first-trade | 交 | Erster Tausch | 1 completed trade | bronze | 60 |
| team-ninja | 忍 | Team Ninja | all six main ninja owned | gold | 150 |
| clean-20 | 星 | Makelloser Scan | 20 scans accepted without correction | silver | 110 |

`first-trade` and `dupes-25` depend on a trading feature that does not exist yet - ship them visible but permanently at 0 progress, or hide the Tausch category until trading lands (decide before implementing).

`streak-7` and `clean-20` need data the app may not record yet: a per-day scan log and a per-scan "was corrected in review" flag. If neither exists, add the flag on the review confirmation path (cheap) and defer the streak achievement.

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

XP: new card +50, duplicate +10, confirmed review scan +10, achievement unlock = its own XP. XP is a **derived total** (recompute from collection + confirmed reviews + unlocked achievements) rather than an incrementing counter - that keeps it correct when a review is later corrected. Store only what can't be derived (e.g. historical trade XP once trading exists).

Background unlocks: rank 4 → Elementarnebel, 6 → Nebelgipfel, 8 → Energie-Dojo, 10 → Goldener Tresor. The last one has no art yet (gradient placeholder in the prototype).

## Unlock moment

Sequence after a scan or a confirmed review group:

1. **Card overlay** (fixed, z 60, blurred backdrop, backdrop click or "Weiter" dismisses).
   - *New card*: rotating conic rays in rarity colour, card reveal with overshoot (`cv-reveal`), name, series + number, rarity chip, "Zu deiner Sammlung hinzugefügt", +50 XP.
   - *Duplicate*: no rays, neutral accent, "Du besitzt diese Karte jetzt N× — bereit zum Tauschen", +10 XP.
2. **Achievement toasts** queue in bottom-right, staggered ~700ms apart, starting ~1.1s after the overlay opens; auto-dismiss ~5.6s; click to dismiss.
3. **Rank-up overlay** replaces the card overlay on dismissal *if* the XP gain crossed a threshold: rank kanji badge, rank name, "Rang N erreicht", and the unlocked background as the stated reward.

Implementation notes carried over from prototype bugs worth not repeating:
- The panel must swallow clicks (`stopPropagation`), otherwise the primary button's click also hits the backdrop and the rank-up step is silently consumed.
- The backdrop needs `overflow:auto` and the panel `max-height:100%`; card art and rank badge use `clamp(...)` against viewport height so the CTA stays visible at ~540px viewport height.

## Rank visibility outside the achievements page

- **Header**: rank name + XP (right-aligned, small) next to a 34px circular kanji badge in the rank's accent colour. Replaces the empty avatar circle. On mobile the text hides, badge stays.
- **Status page**: a "Rang & Erfahrung" block above series progress - badge, "Rang N · Name", "X XP · noch Y XP bis Z", and the same gradient progress bar as series rows.
- **Leaderboard**: rank name as a small uppercase caption under each player name. Sorting stays by owned cards for now; switching it to XP is a separate decision.

## Styling

Reuse the tokens from the visual-refresh change. New additions:
- Tier accents: bronze `oklch(68% 0.11 55)`, silver `oklch(80% 0.03 250)`, gold `oklch(78% 0.15 85)`, legendary `oklch(70% 0.19 300)`.
- Unlocked badge cards use a 160° gradient from `accent / 0.14` into the elevated surface with an `accent / 0.5` border; locked cards are flat `oklch(18% 0.018 290)` with muted text and a dimmed badge.
- New keyframes: `cv-reveal` (scale overshoot), `cv-ray` (16s linear rotation), `cv-toast-in` (slide from right).
- Kanji render in Noto Sans JP, already loaded by the visual-refresh change.

## Open questions

1. **Persistence**: JSON sidecar vs. table for unlock timestamps, selected background, and any non-derivable XP.
2. **Streak data**: is there a per-scan timestamp log today? If not, `streak-7` ships locked.
3. **Trade category**: hide or show-at-zero until trading exists.
4. **Leaderboard sorting**: keep by owned cards, or switch to XP once ranks exist.
