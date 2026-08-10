## Context

The review page (`Review.razor`) already lets a user reassign a photo's `SetName` to a different known series via one button per series. There is no analogous control for `CardNumber`: it's free text (not a fixed catalog list), so it needs a text input rather than a button list. The gRPC layer already has a precedent for single-field updates - `UpdateSetName` and `UpdateReviewStatus` - implemented in `PictureScannerGrpcService.cs` and consumed via `CardCatalogService.cs`. See proposal.md for why this gap matters.

## Goals / Non-Goals

**Goals:**
- Let a reviewer correct a photo's `CardNumber` without leaving the review page or touching sidecar files by hand.
- Keep the same single-field-update pattern already used for `SetName` and `ReviewStatus`, for consistency and minimal surface area.

**Non-Goals:**
- Validating the entered card number against the catalog (e.g. rejecting numbers that don't exist in the selected series). An incorrect value simply lands the photo in the catch-all group, same as today's unrecognized-card-number case - no new validation UX is introduced.
- Batch-correcting card numbers across multiple photos at once.
- Changing how `UpdateSidecar` (the full-field editor) works.

## Decisions

**Add a dedicated `UpdateCardNumber` RPC rather than reusing `UpdateSidecar`.**
`UpdateSidecar` overwrites every editable field, so calling it from the review page would require first reading back the full current record to avoid clobbering other fields - the same reason `UpdateSetName` and `UpdateReviewStatus` exist as narrow, single-field RPCs instead of thin wrappers around `UpdateSidecar`. `UpdateCardNumber` follows that existing precedent exactly (same create-if-missing-with-pending-status behavior, same normalization of blank strings to null).

**Inline text input + explicit save action, not a live/auto-save-on-blur input.**
Card number is free text, unlike the fixed series list, so mistakes are easier to make while typing. An explicit save action (button, or Enter-to-submit) avoids firing an RPC per keystroke or per blur and gives the user a clear moment of intent, matching how "Confirm all" and the series-reassignment buttons are explicit actions rather than implicit ones.

**Reuse the existing `RunPhotoActionAsync` reload/reposition flow.**
`ReassignSeriesAsync` already reloads `groups` via `GetReviewGroupsAsync()` and repositions `currentIndex` to follow the current group (or falls back to a clamped index if the group disappeared). The card-number save action follows the same flow so behavior stays consistent with the series-reassignment control the user is already familiar with.

## Risks / Trade-offs

[A typo'd card number silently lands the photo in the catch-all group instead of surfacing an error] → Acceptable: this matches existing behavior for any unrecognized `SetName`/`CardNumber` combination (see web-card-review-flow's existing catch-all requirements), and the catch-all group is where a reviewer would look next if a correction doesn't seem to have "moved" the photo where expected.

[Extra gRPC surface (new RPC + messages) for a single-field update] → Accepted for consistency with `UpdateSetName`/`UpdateReviewStatus`; introducing a generic "UpdateField" RPC instead would be a bigger, unrequested refactor of the existing editing RPCs.
