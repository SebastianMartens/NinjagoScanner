## Why

Correcting a photo's card number on `/review` to a card that's already owned is the most common review correction. Right now it pays out duplicate-copy XP (+10) and raises the full-screen "Dublette" overlay. That rewards routine clean-up work, and in the common case it interrupts reviewing with a celebration nobody asked for. Duplicates shouldn't be worth XP at all. They're the default outcome of scanning a collection, not an accomplishment.

## What Changes

- **XP formula**: duplicate copies no longer contribute XP. XP sources become: new (distinct) card +50, confirmed review +10, achievement unlock = its own XP, plus stored bonus XP. Because XP is a derived total, this also lowers the XP shown for existing collections that hold duplicates. A collection can therefore drop to a lower rank on its next read. It gets no "rank-down" notification, and already-persisted achievement unlocks are unaffected.
- **Review corrections to a duplicate**: when a review correction (card number, series, or re-analysis) makes a photo match a catalog card that already has another copy, the page raises **no** card overlay and **no** XP notification. Achievement toasts still fire when the correction crosses an achievement (e.g. `dupes-25`), exactly as for a routine "Confirm All".
- **Upload of a duplicate**: still shows the "Dublette" overlay ("Du besitzt diese Karte jetzt N×"), but without an XP gain note, since no XP was earned.
- **Unchanged**: the `dupes-25` achievement ("Doppelt hält besser") still counts duplicate copies and still awards its own 50 XP. Corrections that match a *new* card still show the "Neue Karte" overlay with +50 XP.

## Capabilities

### New Capabilities
- `web-rank-progression`: XP accrual rules (which events earn XP and how much). The gamification change (`archive/2026-09-22-gamification-achievements-ranks`) was archived without syncing specs, so no main spec exists yet. This change introduces only the XP-sources requirement it modifies.
- `web-unlock-feedback`: when the new-card/duplicate overlay is shown and what XP it states. As above, no main spec exists yet. This change introduces only the requirements about duplicate feedback.

### Modified Capabilities
- `web-card-review-flow`: a review correction that resolves to an already-owned card raises no unlock-feedback overlay and no XP notification, only achievement toasts if one unlocks.

## Impact

- `NinjagoScanner.Web/Services/GamificationService.cs`: drop `XpDuplicateCopy` and its term in `ComputeXp`. `DuplicateCopies` stays in `GamificationCollectionState` for `dupes-25`.
- `NinjagoScanner.Web/Components/Pages/Review.razor`: `TryCelebrateCardChangeAsync` falls back to the silent achievement-only path when the new match has more than one owned copy.
- `NinjagoScanner.Web/Services/GamificationCelebrationCenter.cs` and `Components/Shared/UnlockOverlay.razor`: the duplicate overlay carries no XP gain and hides the "+N XP" note.
- `NinjagoScanner.Web.Tests/Services/GamificationServiceTests.cs`: update the XP-sum expectation. Add coverage for no XP from duplicates.
- Existing users: XP totals shrink by 10 × duplicate copies. No data migration is needed, because nothing about duplicate XP is persisted.
- No gRPC, PictureService, or CatalogService changes.
