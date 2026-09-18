## Context

Analysis currently has two entry points in `picture_scanner_service.py`, both of which call
`gemini_service.analyze_card` and write the result with `sidecar_cache.set_from_analysis_result`:

- `UploadPhoto` - new photo, no existing sidecar, no verified pin, runs on the streaming call.
- `Scan` - bulk backfill over every photo in a collection; reads the existing sidecar to (a) skip
  completed analyses, (b) pin a `verified` series/card number via `_verified_match`, and (c) carry
  `review_status` over onto the new result. It stops the whole batch on a transport failure, but
  still writes that photo's `failed` result.

The Review page (`Review.razor`) already drives all per-photo actions through
`RunPhotoActionAsync`: set `busyPhotoId`, run a `PictureServiceClient` call, reload
`CollectionQueryService.GetReviewGroupsAsync()`, re-sync card-number drafts, and re-locate the
current group by key. Per-photo details (`GetCardDetails`) are lazily loaded into a local
`photoDetails` dictionary and never refreshed. See proposal.md for motivation and the specs for
required behavior.

## Goals / Non-Goals

**Goals:**
- One RPC that re-analyzes a single stored photo using exactly the pipeline `UploadPhoto` and
  `Scan` use - no second analysis code path to keep in sync.
- Re-analysis semantics identical to `Scan`'s treatment of an existing sidecar (verified pin,
  review status preserved), plus safe handling of a transient Gemini outage.
- A Review-page button whose long-running behavior (seconds, two Gemini calls) doesn't freeze or
  mis-report the rest of the page.

**Non-Goals:**
- Group-level or bulk re-analysis from the UI; a Collection-page button.
- Changing `Scan`'s skip rules or making `Scan` reuse the new helper's failure semantics.
- Cancelling an in-flight re-analysis, queuing, or rate limiting.

## Decisions

### A dedicated `ReanalyzePhoto` RPC rather than reusing `Scan`
`Scan` already re-analyzes existing photos with `overwrite_existing_sidecars`, but it is a
collection-wide batch: no per-photo filter, per-request inter-photo delays, and a summary-only
response. Adding a `photo_id` filter to it would overload a batch admin RPC with an interactive
one and still not return the updated card. A new unary RPC
`ReanalyzePhoto(ReanalyzePhotoRequest{photo_id, collection_id}) -> ReanalyzePhotoResponse{CardEntry card}`
matches the shape of `UploadPhoto`'s response, so Web can reuse `ToCardListItem`. The request
deliberately omits `UploadPhotoMetadata`'s `api_key`/`model`/`catalog_service_address`/retry
overrides: those exist for tests and ad-hoc callers, Web never sets them, and PictureService's
environment config (`ScannerConfig.load_for_upload`) is the source of truth. The request message
therefore has only the two id fields; overrides can be added additively if ever needed.

Alternative considered: delete-and-re-upload from Web using the photo bytes. Rejected - changes
the `photo_id`, needs the bytes round-tripped through Web, and loses the sidecar's review status.

### Factor the shared analyze step out of `UploadPhoto`
`UploadPhoto` and `ReanalyzePhoto` both do: load config → require API key → load catalog snapshot
→ `analyze_card` with a catch-all converting unexpected exceptions into a `failed` result. This is
extracted into one private helper on `PictureScannerService` taking
`(config, image_bytes, photo_id, source_file_name, verified)` and returning the
`CardAnalysisResult`; both RPCs call it, keeping their own precondition/abort handling. `Scan`
is left alone - its config/error handling differs (summary-with-flag instead of aborting) and the
change stays reviewable. This is the "no second code path" goal without a wider refactor.

### Failure semantics differ from `Scan`: a transport failure does not overwrite the sidecar
`Scan` writes a `failed` sidecar even for transport failures because it is backfilling photos that
usually have no good analysis yet. `ReanalyzePhoto` typically targets a photo that *does* have one
the user wants to improve, and a Gemini outage would silently downgrade it to `failed`. So when
`result.is_transport_failure` is set the RPC aborts `UNAVAILABLE` *before* writing anything. Content
failures (`failed` without the transport flag) are still recorded, so the user sees why in the
details and can fix by hand - same as every other entry point. `UploadPhoto` keeps recording
transport failures because there the photo has no prior result to protect.

Alternative considered: always write, and let Web warn. Rejected - data loss on the happy-path
retry ("Gemini was down, click again") is the exact scenario the button will be used in.

### Read the sidecar twice: before to pin, after to preserve review status
The verified pin must be determined from the sidecar as it stands *before* the analysis starts.
But the analysis takes seconds, and a person may set a review status on another tab or by fast
clicks during that window. Carrying the *pre-analysis* review status onto the result (as `Scan`
does) would silently revert that. So: read sidecar → compute `verified` pin → analyze → re-read
sidecar from the cache → copy *that* record's `review_status` onto the result → write. A residual
race between the second read and the write is one in-process `await` apart (the cache/DynamoDB
write is the same non-transactional single-item put every other RPC does) and is accepted.

Stale-write of the *other* fields is not a concern: the result intentionally replaces them.

### Web: track re-analysis per photo in a set, not in `busyPhotoId`
`busyPhotoId` holds one id and is unconditionally nulled in `RunPhotoActionAsync`'s `finally`. A
re-analysis lasts seconds, so a user will plausibly start a second one (or click a status button)
on another tile while the first runs; sharing `busyPhotoId` would release the first tile early or
mis-attribute busy state. A separate `HashSet<string> reanalyzingPhotoIds` is added, and `IsBusy`
becomes "confirming all, or `busyPhotoId` matches, or the id is in the set". Because a Blazor
Server component re-renders on each awaited continuation, the set-add before the await is what
makes the in-progress state visible immediately.

The page reload after completion reuses the existing reload-and-relocate logic from
`RunPhotoActionAsync` (reload groups, `SyncCardNumberDrafts`, find current group by key else
clamp). That logic is currently duplicated between `RunPhotoActionAsync` and `ReassignSeriesAsync`;
this change moves it into one `ReloadGroupsKeepingPositionAsync` helper used by both plus the new
action, rather than adding a third copy. `RunPhotoActionAsync` itself is not reused for
re-analysis because it drives `busyPhotoId` (see above).

### Errors are shown on the tile; other actions' (absent) error handling is left alone
Existing per-photo actions have no error handling - a failed gRPC call surfaces as a circuit
error. That's not acceptable for this action: Gemini/CatalogService outages are an expected,
recoverable outcome. The re-analysis handler catches `RpcException` (and any other exception from
the call), stores a short German message in a `Dictionary<string, string> reanalysisErrors`
keyed by photo id, and clears that entry when the next attempt for the photo starts. The message
doesn't expose the raw gRPC status text; it distinguishes only "service currently unavailable, try
again" (`Unavailable`/`FailedPrecondition`) from a generic failure. Retrofitting the other actions
is out of scope.

### Refresh the cached details when a photo is re-analyzed
`photoDetails[photoId]` is a one-shot lazy cache. After a successful re-analysis the entry is
removed; if the tile is currently expanded it's immediately re-fetched via `GetCardDetailsAsync`,
otherwise the next expand fetches fresh data. Without this the tile would keep showing the old
scanned-at time, error message, and attributes JSON.

### Button placement and styling
The button goes in `review-photo-actions`, next to "Löschen", labelled "Neu analysieren" and
"Analysiere..." while running, using the existing `review-btn` class - no new visual language.
It is not styled as dangerous and needs no confirmation: re-analysis is non-destructive to review
state and the user asked for it explicitly.

## Risks / Trade-offs

- **[Re-analysis discards hand-edits on unverified photos]** A person who corrected a card number
  by hand but never marked it `verified` loses it → the spec states this explicitly (mirrors
  `Scan` with overwrite), the review flow's natural order is edit → verify, and "verify to pin"
  is the documented way to protect an edit. No extra confirmation dialog is added; revisit if it
  bites in practice.
- **[Gemini cost from repeated clicks]** Each click is two model calls → the button is disabled
  while in flight; no server-side throttle since Web is the only caller and traffic is one
  interactive user.
- **[Long-running unary RPC]** Blocks a gRPC call for several seconds (up to
  `timeout_seconds`/retries from `ScannerConfig`) → Web's channel default deadlines must allow it;
  verify the channel has no shorter deadline than `UploadPhoto`, which already runs the same
  analysis inline. If the Blazor circuit disconnects mid-call the analysis still completes
  server-side and the result is simply visible on next load.
- **[Second read of the sidecar isn't atomic with the write]** → accepted, see Decisions.
- **[Depends on the unmerged staged-analysis pipeline]** The verified-pin parameter and
  `is_transport_failure` flag already exist in the working tree from that change → implement after
  it lands, or rebase onto it.

## Migration Plan

Purely additive: a new RPC and a new UI control. Deploy PictureService before Web so the RPC exists
when the button ships; a Web build calling an older PictureService would get `Unimplemented`,
which surfaces as the tile's generic error message. Rollback is reverting either service
independently.
