## Context

See proposal.md - Why for the measured cost and the history this reverses part of. Three call sites in `NinjagoScanner.Web` currently read `CardEntry.download_url` from an otherwise-unscoped `ListCards`/`ListCardEntriesAsync` call: Review (`PictureServiceClient.GetCardsAsync` → `ToCardListItem`), Gallery (`CollectionQueryService.GetGalleryCardsAsync`), and the Collection page's per-card photo list (`CollectionQueryService.BuildCardPhotosAsync`). `GetCollectionOverviewAsync`, `GetSeriesSummaryAsync`, and the outer `ListCardEntriesAsync` call inside `GetCollectionCardDetailsAsync` only use photo entries for counting/grouping/matching and never touch the field — unaffected here, as they were when `2026-08-27-inline-photo-download-urls` made the same determination.

## Goals / Non-Goals

**Goals:**
- Bound the number of download URLs resolved per page load to what that page actually displays, for both Review and Gallery.
- Keep round-trip count at one bounded follow-up call per view (never one call per photo), preserving what `2026-08-25-batch-photo-download-urls`/`2026-08-27-inline-photo-download-urls` already solved.
- Preserve `web-card-review-flow`'s existing "Photo display URLs stay stable while the user works on the page" guarantee unchanged.

**Non-Goals:**
- Reducing `ListCards`'s own full-collection read cost (DynamoDB scan + S3 listing) — stays O(N), out of scope per proposal.md.
- Prefetching the next/previous group's URLs ahead of navigation. Worth doing later, but it adds its own complexity (canceling a stale prefetch when the user jumps around quickly) that isn't needed to fix the measured problem; the bounded call this change adds is fast enough (tens of photos, not thousands) that resolving on-demand at navigation time is unlikely to be perceptibly slower than a prefetch would have been.
- Changing `GetPhotoDownloadUrl` (the existing singular RPC) — unrelated, left as-is.

## Decisions

**Revive the batch RPC's shape rather than inventing a new one.** `2026-08-25-batch-photo-download-urls`'s `GetPhotoDownloadUrls(photo_ids) -> {photo_id: download_url}` shape already solved "resolve many at once, bounded by a caller-supplied list." Reusing it (same RPC name, same request/response shape) keeps the historical link explicit and avoids re-litigating a design that was already reasoned through once, this time justified by scoping rather than round-trip count.

**Missing photo IDs are omitted from the response, not an error.** Both Review and Gallery build their photo-ID list from locally-held data that can be a moment stale relative to the backend (Review: a photo just deleted by a concurrent action or a prior local edit not yet reconciled; Gallery: a photo count changed since the series was last selected). Failing the whole bounded call for one stale ID would break every other, still-valid photo's display for no benefit — the caller already treats "no URL" as "show nothing/a placeholder" for a photo it can't display, so omission degrades gracefully at the one photo instead of the whole request.

**Review resolves missing URLs on every render of the current group, not only once at page load.** Under the old design every `CardEntry` always carried a URL, so a photo entering the displayed group via a local edit (e.g. `PickSeriesAsync` reassigning its series to match the current group, or a card number correction) already had one. Under this design it might not — it may never have been in a previously-displayed group. The fix: whenever the group shown to the user changes (initial load, next/previous navigation, restart-from-beginning) or the current group's membership changes (a local edit adds or removes a photo), the page checks which of that group's `DisplayedPhotos` (≤18, the existing cap) lack a resolved URL and issues one bounded call for exactly those. A photo that already has a URL is never included in that call, which is what keeps the "stays stable" requirement intact — the mechanism only ever fills a gap, never refreshes an existing value.

**Gallery resolves URLs for the matched photos of the selected series only, after computing the match.** `GetGalleryCardsAsync` already computes, per catalog card in the selected series, which one photo (if any) is the deterministic match (`OrderBy(PhotoId).FirstOrDefault()`). The bounded call is built from exactly that already-computed list of `PhotoId`s — no separate "which photos are in this series" step is needed beyond what the existing matching logic already produces.

**`BuildCardPhotosAsync` converts mechanically.** It already filters `entries` down to one ownership key (one catalog card) before doing anything with a download URL, so the change there is swapping a field read for a bounded call over the same, already-small photo-ID list — no new scoping logic needed.

## Risks / Trade-offs

- **Deployment ordering creates a compatibility window.** If PictureService deploys first, old Web code reading `CardEntry.download_url` gets an absent/empty field until Web deploys — photos would appear broken (no image) for that window, not error out, since `ImageUrl` is nullable/optional in the existing models. If Web deploys first, it calls an RPC that doesn't exist yet — `Unimplemented` errors on every URL resolution. → Mitigation: deploy PictureService first (matches the proposal's stated ordering and the same pattern `2026-08-27-inline-photo-download-urls` used), accepting a brief "no images" window over a brief "hard error" window; keep the window short by deploying Web immediately after.
- **A caller that forgets to scope its photo-ID list defeats the whole point.** Nothing at the type level stops a future call site from passing every photo ID in the collection to the new bounded RPC, recreating the original problem one call site at a time. → Mitigation: the spec scenarios ("Only the requested photos are resolved" etc.) and this design doc make the intended usage explicit; review of any new call site against `picture-service-photo-download`'s spec is the guard, same as for any other capability contract in this repo.
- **Two round trips instead of one for a page that used to get everything from a single call.** Review/Gallery's total request count per view goes from 1 (list only) to 2 (list, then resolve). This is the intended trade-off — a small, bounded second call in exchange for the first call no longer doing O(N) presigning work — and is explicitly allowed by the reworded `web-card-review-flow` requirement ("at most one bounded follow-up request").

## Migration Plan

1. Deploy PictureService: add the new RPC, stop populating `CardEntry.download_url` in `ListCards`.
2. Deploy `NinjagoScanner.Web`: switch Review, Gallery, and the Collection detail view to the new bounded call; stop reading `CardEntry.download_url`.

Rollback: redeploy the previous PictureService and Web versions together (this is a breaking proto-level change in both directions — an old Web version needs the field populated; a new Web version needs the new RPC — so rollback, like forward deployment, requires both services to move together, same as the two prior related changes).
