## Context

See proposal.md - Why. Two things make this cross-cutting rather than a single-file tweak:

1. The closed language set (`de`/`en`/`unknown`) is duplicated in four places that must move together: the Gemini prompt/schema, `GeminiApiService.NormalizeLanguage`, the `Languages` constants class (hand-duplicated once in `NinjagoScanner.PictureService/ScannerModels.cs` and once in `NinjagoScanner.Web/Models/CardListItem.cs` — there is no shared project between the two), and the Collection page's `<select>`.
2. The gRPC contract between Web and PictureService is defined in two hand-mirrored `.proto` files (`NinjagoScanner.PictureService/Protos/picture_service.proto` and `NinjagoScanner.Web/Protos/picture_service.proto`) with no shared proto project — every new RPC is written twice and must stay byte-identical.

Both duplication patterns already exist for `SetName`/`CardNumber`/`ReviewStatus` and their `Update*` RPCs; this change follows the same pattern for `Language` rather than introducing a new one (a shared-project refactor is out of scope here).

## Goals / Non-Goals

**Goals:**
- Add `pl` as a fourth recognized language value, consistently, everywhere the closed set is currently checked.
- Let a reviewer fix a photo's language from the Review page in one click, without navigating to the Collection page's full edit form.
- Reuse the existing `UpdateSetName`/`UpdateCardNumber`/`UpdateReviewStatus` RPC and Blazor UI patterns exactly, so the new code is unsurprising to anyone familiar with those three.

**Non-Goals:**
- No change to how language affects card identity, ownership, or grouping — it stays purely descriptive (per the GLOSSARY.md Language entry) and out of `BuildOwnershipKey`/group-resolution logic.
- No shared class library to de-duplicate the `Languages` constants or the `.proto` files — out of scope; this change follows the existing duplication pattern rather than fixing it.
- No retroactive re-analysis of existing Polish cards' sidecars — a human corrects them via the new dropdown; Gemini simply becomes able to detect `pl` going forward.
- No enum type (`enum CardLanguage`) — the existing code represents language as a plain `string` closed set (both in C# and in the `.proto`), and this change keeps that representation rather than introducing a typed enum.

## Decisions

**Add `UpdateCardLanguage` as a fifth single-field RPC, not a `Language` field on `UpdateCardNumberRequest` or similar.** Mirrors `UpdateSetName`/`UpdateCardNumber`/`UpdateReviewStatus` exactly: one RPC per independently-correctable field, each a thin wrapper around "load-or-create pending sidecar, set one field, save." Alternative considered: extend `UpdateSidecar` (the full-overwrite RPC) instead of adding a narrow RPC — rejected because the Review page's per-photo tiles need to change exactly one field without needing to know/send the photo's other current field values, exactly the reason the other three narrow RPCs exist.

**Server-side `UpdateCardLanguage` does not validate against the closed set.** Matches current behavior of `UpdateSidecar`, `UpdateSetName`, and `UpdateCardNumber`, none of which validate their string inputs against a closed set server-side — normalization (blank → null) is the only transformation applied. The closed set is enforced by the UI (`<select>` with fixed options) and by `GeminiApiService.NormalizeLanguage` for values coming from Gemini. Consistent, not a new gap introduced by this change.

**`CardListItem.Language` defaults to German when absent**, matching `ToCollectionSidecar`'s existing default and the `picture-service-card-listing` spec's defaulting rule (`CardEntry.Language` defaults to `de`). This keeps the Review page's dropdown pre-fill consistent with what the Collection page already shows for the same photo.

**Language correction on Review.razor does not trigger `RunPhotoActionAsync`'s group-repositioning logic**, even though it's convenient to reuse that helper for the reload-and-resync part. Language is confirmed independent of the series+card-number grouping key (see proposal.md - Impact and the GLOSSARY.md Language entry), so after a language save the photo stays in its current group — no "which group does this photo now belong to" resolution is needed, unlike the series-reassignment and card-number controls.

## Risks / Trade-offs

- **Two hand-duplicated `Languages` classes and two hand-mirrored `.proto` files must be edited in lock-step.** → Existing risk, not introduced by this change; mitigated the same way the codebase already mitigates it for the other three fields — by keeping both edits in the same commit and letting the PictureService test suite catch drift.
- **No server-side closed-set validation on `UpdateCardLanguage` means a caller could write an arbitrary string via a hand-crafted gRPC request.** → Accepted, matching existing behavior of the other three single-field RPCs; not a new risk specific to this change.

## Open Questions

None — the RPC shape, defaulting behavior, and UI pattern all have a direct precedent (`UpdateCardNumber`) to follow.
