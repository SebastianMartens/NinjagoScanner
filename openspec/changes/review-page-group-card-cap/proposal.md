## Why

The review page renders every photo in the currently displayed group at once. The catch-all group ("Ohne bekannte Serie" — photos whose `SetName`/`CardNumber` don't resolve to a catalog card) can hold thousands of photos, and rendering that many photo tiles (each with an image, a status control, a full series picker grid, and edit controls) at once makes the page very slow to load and interact with.

## What Changes

- The review page displays at most 18 photos per group at a time, in the group's existing photo order.
- When a group has more than 18 photos, the page shows a message stating that more photos exist beyond the ones displayed, instead of silently truncating.
- "Confirm all" continues to act only on the photos currently shown for the group (unchanged wording, now bounded by the 18-photo cap), so confirming a group with more than 18 photos no longer confirms photos beyond what's visible.

## Capabilities

### Modified Capabilities
- `web-card-review-flow`: the "All photos in a group are shown at once" requirement changes to cap simultaneously displayed photos per group at 18, and a new requirement adds the "more photos exist" message shown when a group exceeds that cap.

## Impact

- `NinjagoScanner.Web/Components/Pages/Review.razor`: the photo grid renders `group.Photos.Take(18)` instead of every photo; a message is shown when `group.Photos.Count > 18`.
- No change to `CollectionQueryService`, `CardReviewGroup`, or PictureService — grouping, filtering, and the group photo count label (`@group.Photos.Count Foto(s)`) still reflect the full group.
