## Why

On the review page (`/review`), a photo tile's `ReviewStatus` is currently invisible until a reviewer clicks "Confirm" or "Has Error" themselves — there is no badge, icon, or label showing whether a photo is `unreviewed`, `verified`, or `incorrect`, even in the expanded details section. This makes it hard to tell at a glance which photos in a group still need attention, especially after using "Confirm all" or returning to a partially-reviewed group. Additionally, the separate "Confirm" and "Has Error" buttons duplicate information a status indicator would already show, and there is no way to undo a mistaken Confirm/Has Error click short of editing the sidecar elsewhere.

## What Changes

- Replace the separate "Confirm" and "Has Error" buttons on each photo tile with a single three-segment status control (`Unreviewed` / `Verified` / `Incorrect`) that both displays and sets that photo's `ReviewStatus`.
- The segment matching the photo's current `ReviewStatus` is visually highlighted; activating any segment sets that photo's `ReviewStatus` to the corresponding value.
- This adds a new capability: a photo can now be reverted back to `unreviewed` by clicking that segment, which was not previously possible from the review page.
- The control updates immediately (highlighted segment moves) when `ReviewStatus` changes via the control itself or via "Confirm all", without a page reload.
- Reuse the existing pill-badge visual style (`.table-status` classes: `status-ok`/`status-pending`/`status-failed`) and German status labels already used on the `CardsTable` view, adapted into a three-segment toggle group, for visual consistency across the app.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `web-card-review-flow`: the "Confirm"/"Has Error" controls are replaced by a combined status display+control with a third, previously unavailable option to revert a photo to `unreviewed`.

## Impact

- `NinjagoScanner.Web\Components\Pages\Review.razor`: replace the `review-btn-confirm`/`review-btn-error` buttons (~lines 71-72) with a three-segment status control, and add a status→class/label mapping (reusing or sharing the pattern from `CardsTable.razor` lines 346-374). The existing `ConfirmPhotoAsync`/`FlagPhotoErrorAsync` handlers already call the generic `CardCatalogService.UpdateReviewStatusAsync(fileName, status)` (lines 187-194), so reverting to unreviewed only needs a third handler passing `ReviewStatuses.Unreviewed` - no service or contract changes.
- `NinjagoScanner.Web\wwwroot\app.css`: reuses existing `.table-status` / `.status-ok` / `.status-pending` / `.status-failed` classes; may need small additions for the segmented/toggle-group layout (grouping three segments together, highlighting the active one).
- No changes to gRPC contracts, sidecar data, or other services — this is a Web-only, presentation-layer change.
