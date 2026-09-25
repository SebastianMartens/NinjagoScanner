## 1. XP formula

- [x] 1.1 In `NinjagoScanner.Web/Services/GamificationService.cs`, remove `XpDuplicateCopy` and the `state.DuplicateCopies * XpDuplicateCopy` term from `ComputeXp`. Keep `DuplicateCopies` in the state for `dupes-25`. Verify with `dotnet build NinjagoScanner.slnx`, which surfaces every remaining reference to the constant.
- [x] 1.2 Update `GetXpAsync_SumsAchievementCardDuplicateAndReviewXp` in `NinjagoScanner.Web.Tests/Services/GamificationServiceTests.cs` to expect no duplicate XP (`20 + 2 * 50 + 1 * 10`) and rename it to reflect that duplicates add nothing. Add a test where a second copy of an owned card leaves XP unchanged. Verify with `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~GamificationServiceTests"`.

## 2. Unlock feedback

- [x] 2.1 In `GamificationCelebrationCenter.CelebrateCard`, set `XpGained` to `XpNewCard` for `NewCard` and `0` for `Duplicate`. Verify with a build.
- [ ] 2.2 In `Components/Shared/UnlockOverlay.razor`, omit the `.unlock-xp-note` element when a card variant has `XpGained == 0`, and keep "Rangaufstieg" for `RankUp`. Verify by uploading a photo of an already-owned card on `/upload`: the "Dublette" overlay shows "Du besitzt diese Karte jetzt N×" and no "+N XP" line. A new card still shows "+50 XP".
- [ ] 2.3 In `Review.razor` `TryCelebrateCardChangeAsync`, when `ownedCopies > 1` after the correction, call `CelebrateSilentlyIfAnythingUnlockedAsync(xpBefore)` and return instead of raising the card overlay. Verify on `/review`: correcting a photo's card number (and separately, its series) to an already-owned card moves the photo to that group with no overlay or toast. Correcting to an unowned card still shows "Neue Karte" with "+50 XP".

## 3. Cleanup and verification

- [x] 3.1 Grep `NinjagoScanner.Web` for leftover duplicate-XP references (`XpDuplicateCopy`, "+10 XP", duplicate XP in UI copy such as the `/achievements` caption and About page) and fix any. Verify the grep comes back empty.
- [x] 3.2 Run `dotnet test NinjagoScanner.slnx` and verify every test passes.
