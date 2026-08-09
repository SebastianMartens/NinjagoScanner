## 1. Status control markup and styling

- [x] 1.1 Replace the `review-btn-confirm`/`review-btn-error` buttons in the photo tile markup in `NinjagoScanner.Web\Components\Pages\Review.razor` with a three-segment status control (`Unreviewed` / `Verified` / `Incorrect`).
- [x] 1.2 Add a helper (or reuse/share the one in `CardsTable.razor`) that maps a photo's `ReviewStatus` (`unreviewed`/`verified`/`incorrect`) to the corresponding CSS modifier class (`status-pending`/`status-ok`/`status-failed`) and German label, and apply it to highlight the segment matching the photo's current `ReviewStatus`.
- [x] 1.3 Add/adjust CSS in `app.css` as needed to group the three segments visually as a single control and distinguish the highlighted (active) segment from the other two.

## 2. Behavior

- [x] 2.1 Wire the `Verified` and `Incorrect` segments to the existing `ConfirmPhotoAsync`/`FlagPhotoErrorAsync` handlers (`Review.razor` lines 187-194). *(Implemented as a single shared `SetReviewStatusAsync(photo, reviewStatus)` handler used by all three segments instead of two separate methods - same underlying `UpdateReviewStatusAsync` call, less duplication.)*
- [x] 2.2 Add a handler for the `Unreviewed` segment that calls `CardCatalogService.UpdateReviewStatusAsync(photo.ImageFileName, ReviewStatuses.Unreviewed)`.
- [x] 2.3 Ensure the highlighted segment re-renders immediately after any of the three segment actions, and after "Confirm all" updates a photo's `ReviewStatus` in component state, without a page reload.

## 3. Verification

- [x] 3.1 Manually verify in the browser: each photo tile on `/review` shows the three-segment control with the segment matching its current review status highlighted; clicking `Verified`/`Incorrect`/`Unreviewed` updates that photo's status and the highlighted segment immediately; "Confirm all" highlights `Verified` on every tile in the group; no separate Confirm/Has Error buttons remain.
