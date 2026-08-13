## Context

`ReviewStatus` (`unreviewed` / `verified` / `incorrect`) already exists end-to-end: sidecar field → gRPC `UpdateReviewStatus` → `CardCatalogService.UpdateReviewStatusAsync` → Review page's three-segment control, whose `incorrect` segment is already labeled "Fehlerhaft" (`Review.razor:335`). The Gallery "Fehlerhaft" button is a second entry point onto that same field, not a new concept.

`GalleryCardItem` (`Models/GalleryCardItem.cs`) currently exposes only `ImageUrl`, derived in `CardCatalogService.GetGalleryCardsAsync` from the first matched `CardEntry` (`matchedPhoto`, ordered by file name). That same `matchedPhoto` already carries `ImageFileName` and `Rarity`; the method just doesn't surface them past `ImageUrl`/`Rarity` today.

Deletion has no existing counterpart anywhere in the app — `PictureScannerGrpcService` and `SidecarStore`/`SidecarCache` only ever write. `SidecarCache` is a `ConcurrentDictionary<string, SidecarRecord?>` keyed by `Path.GetFullPath(sidecarPath)`; there's no eviction path yet, only get/set.

Proto files are duplicated verbatim between `NinjagoScanner.Web/Protos/` and `NinjagoScanner.PictureService/Protos/` (no shared project) — every existing RPC was added to both copies, and `DeletePhoto` follows the same pattern.

## Goals / Non-Goals

**Goals:**
- Reuse the existing `ReviewStatus`/`incorrect` field and pipeline for the Gallery flag button — no new field, no new sidecar concept.
- Add one new gRPC RPC (`DeletePhoto`) that deletes both files and keeps `SidecarCache` correct, following the existing RPC/service/service-client layering.
- Reuse the Review page's existing per-photo action plumbing (`RunPhotoActionAsync`, `busyPhotoFileName`, index recovery after a group changes) for delete instead of inventing a parallel mechanism.

**Non-Goals:**
- No undo/trash/soft-delete — the proposal asks for permanent deletion.
- No bulk delete or bulk Fehlerhaft-flagging across a whole group; both are single-photo actions, matching the granularity every other Review/Gallery action already uses.
- No change to how photos are matched to catalog cards (ownership key logic untouched).

## Decisions

**Gallery button sets `ReviewStatus=incorrect` directly, no new "wrongly assigned" field.**
The request's "Fehlerhaft" wording and the Review page's existing `incorrect` segment (labeled "Fehlerhaft") already mean the same thing: a human decided this photo doesn't belong where it landed. Introducing a second flag would create two overlapping concepts a user has to reconcile. Alternative considered: a dedicated `IsMisassigned` boolean — rejected, since it would need its own filter, its own display logic, and a decision about how it interacts with `ReviewStatus` for no behavioral gain.

**`GalleryCardItem` gains `ImageFileName` and `ReviewStatus`.**
The Fehlerhaft button needs `ImageFileName` to call `UpdateReviewStatusAsync`; the flagged-state indicator needs `ReviewStatus` to render. Both come straight from the `matchedPhoto` already loaded in `GetGalleryCardsAsync` — no new query.

**The photo tile is restructured so the new button never nests inside the existing lightbox button.**
Once the Gallery markup was examined directly, nesting a second `<button>` for Fehlerhaft inside the existing single `<button class="gallery-tile-photo" @onclick="OpenLightbox">` (wrapping the image, caption, and tags) turned out to be invalid HTML with unreliable click/focus behavior. Resolution: move the lightbox click handler onto the inner `gallery-tile-media` element (rendered as its own `<button>`, sized/styled identically to today's outer tile), so the outer tile becomes a plain container and the non-puzzle Fehlerhaft button can sit alongside it as a sibling control in the caption area.

**Puzzle sub-group tiles expose Fehlerhaft through the existing lightbox instead of a new on-tile control or a new selection gesture.**
Puzzle tiles have a hard "no caption element of any kind" rule (`web-gallery-page`'s "Puzzle Tiles Show Only the Photo or Placeholder Graphic" requirement — the photo-count badge is already excluded from puzzle tiles for the same reason), so a persistent on-tile Fehlerhaft button was never an option there; the first version of this change shipped without any way to flag a puzzle card from Gallery at all. When asked how a puzzle tile should let the user "select" a card to reveal the control, three options were on the table: (a) repurpose the tile's own click into a select-then-zoom two-step, (b) add a small always-visible per-tile toggle icon, or (c) put the control inside the lightbox that a puzzle tile's existing click already opens. Went with (c): puzzle tiles already open a lightbox on click (the "Tile Click Opens In-Place Photo Zoom" requirement, unchanged by this revision), so that lightbox already *is* the tile's "this card is selected" state — no new gesture, no new always-visible chrome on the condensed grid, and the click-to-zoom behavior for puzzle tiles is completely unchanged. `GalleryCardItem.Category` already flows into `lightboxCard`, so puzzle-ness inside the lightbox reuses the same `IsPuzzleCategory` helper the grid already uses — no new state. Non-puzzle tiles are unaffected: they keep their existing on-tile button and do not also get a lightbox button, since that would just duplicate an already-visible control.

**Marking Fehlerhaft no longer reuses `LoadSeriesCardsAsync`'s full reload.**
`LoadSeriesCardsAsync` toggles `isLoading` (showing "Lade Galerie..." in place of the whole grid) and unconditionally clears `lightboxCard`. That was fine when Fehlerhaft was tile-only (no lightbox involved), but it breaks the puzzle/lightbox flow: the dialog would slam shut the instant the user flagged a photo, before they could see the control update to its flagged state. `MarkFehlerhaftAsync` now calls a lighter `ReloadSeriesCardsAsync` that refreshes `seriesCards` without the loading flicker and, if a lightbox was open, re-points `lightboxCard` at the freshly-reloaded card with the same `ImageFileName` (or closes it if that photo is gone) — keeping the dialog open with an updated, now-disabled control. This also smooths out the non-puzzle tile-click path, which no longer flashes the full-page loading state for a single-field update.

**No unmark/toggle control on the Gallery tile.**
The proposal only asks to *mark* a card as wrongly assigned from Gallery. Reverting is already possible via the Review page's existing three-segment control, which is the page whose whole purpose is correcting review state. Adding a second correction surface on Gallery would duplicate that control for a case (undoing a Gallery-side mis-click) that's rare enough to route through Review.

**`DeletePhoto` is a new unary RPC on `CardPictureService`, not folded into an existing `Update*` RPC.**
Every existing `Update*` RPC is additive/idempotent and creates a pending sidecar if none exists; delete is destructive and must fail on a missing image rather than silently no-op. Keeping it a separate RPC keeps that error behavior (`NotFound` when the image doesn't exist) from leaking into the update RPCs' create-if-missing contract.

**`DeletePhoto` fails closed if the image file doesn't exist, but treats a missing sidecar as fine.**
An image is required for the operation to mean anything; a sidecar may legitimately not exist yet (e.g. an uploaded-but-unscanned photo), matching how `ListCards` already tolerates a missing sidecar (`AnalysisStatus: "pending"`).

**`SidecarCache` gets a `Remove(sidecarPath)` method, called after the file deletions succeed.**
Symmetric with `SetAsync`: normalize the path the same way (`Path.GetFullPath`), then `TryRemove`. Without this, a photo deleted mid-session could keep serving its last-cached sidecar to `ListCards` until the process restarts.

**Delete confirmation is an in-page modal dialog, following the Gallery lightbox's existing backdrop pattern (`gallery-lightbox-backdrop`/`@onclick:stopPropagation`), not a browser `confirm()`.**
Blazor's `@onclick:stopPropagation` + backdrop-click-to-dismiss pattern is already established on this page family (Gallery's lightbox, Review's series popover); a native `confirm()` would be inconsistent with that and harder to style/test. The dialog only needs a photo file name and two actions (confirm/cancel), so a single reusable state field (`photoPendedForDeletion: CardListItem?`) on Review.razor is enough — no new component needed given the codebase's existing pattern of inline dialog markup per page.

**Post-delete index recovery reuses the group-key-lookup pattern from `RunPhotoActionAsync`/`ConfirmAllAsync`, not a new algorithm.**
`RunPhotoActionAsync` already handles "the current group's key might not resolve to the same index after a refresh" by re-finding the group by key and falling back to `Math.Min(previousIndex, count-1)`. Deleting the last photo in a group removes the group entirely, which is exactly the case `ConfirmAllAsync`'s `FindNextFilteredIndexAfter` was built for (advance past a key that's no longer in the list). Delete reuses that same helper rather than a bespoke one, since "the current group might disappear from the filtered list" is identical in both cases.

## Risks / Trade-offs

- [Deleting a photo is irreversible and the confirmation dialog is the only safeguard] → Dialog must state plainly that the action is permanent; no further mitigation planned since the proposal explicitly asks for permanent deletion, not soft-delete.
- [Two proto files must be kept identical by hand] → Pre-existing convention risk (every prior RPC crossed this), not introduced by this change; task list will call out updating both copies explicitly.
- [Concurrent request deletes a photo while another request is mid-update to its sidecar] → Out of scope: the app has no concurrency control anywhere else in the sidecar-write path (`UpdateSidecar` et al. already have this same race), so `DeletePhoto` matches existing behavior rather than solving a pre-existing gap.

## Open Questions

None — the confirmation dialog styling and the Gallery flagged-state indicator's exact visual treatment are implementation detail for tasks.md to work out against the existing `app.css` design language, not a decision that changes the spec or approach.
