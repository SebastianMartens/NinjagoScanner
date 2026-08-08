## Context

See proposal.md - Why. Relevant existing building blocks this design reuses rather than reinvents:

- `CardCatalogService.GetKnownSeriesAsync()` (Web) already returns known series names via CatalogService's `ListSeries` RPC, whose repository already sorts entries by `SortOrder` server-side (`CatalogRepository.cs:112`) before the response leaves CatalogService. `CardsTable.razor`'s existing `GetSeriesGroupRank` already relies on this ordering (array index = rank) — the review page reuses the same assumption instead of adding a new API.
- `CardCatalogService.LoadCardEntriesAsync()` / `ListCards` already returns every photo's full sidecar data (`CardEntry`), including `SetName` and `CardNumber` — everything needed to group photos client-side.
- `UpdateSetName` RPC already exists and already does exactly what the per-photo series-reassignment buttons need (touch only `SetName`), so it's reused unchanged.

## Goals / Non-Goals

**Goals:**
- Define how photo grouping/sorting is computed and where.
- Define the new `UpdateReviewStatus` RPC's shape and how it's used for both single-photo and group-level actions.
- Define what happens to page state across the mutating actions (reassign, confirm, confirm-all).

**Non-Goals:**
- Per-series statistics on the new Overview page (explicitly deferred by the proposal).
- Any change to how `AnalysisStatus` is computed or to the Gemini scan pipeline itself.
- Server-side/catalog-driven grouping (rejected - see Decisions).

## Decisions

**Grouping is computed in the Web project, not a new backend endpoint.**
Group-by-(`SetName`,`CardNumber`) is pure presentation logic over data `ListCards` already returns in full. Adding a dedicated grouping RPC would duplicate `ListCards`'s data and couple PictureService to a UI concern. `CardCatalogService` gets a method that loads card entries (existing `LoadCardEntriesAsync`) and known series (existing `GetKnownSeriesAsync`), then groups/sorts them the same way `CardsTable.razor`'s `GetSeriesGroupRank` already ranks set groups today - reusing that convention rather than inventing a second one.

**Catch-all group key.** All photos whose `SetName` doesn't match a known series (or is blank) collapse into one group keyed by a sentinel (e.g. `null`/empty group key), sorted with rank `int.MaxValue` exactly like `GetSeriesGroupRank`'s existing fallback - consistent with the ordering already used elsewhere in the app.

**New `UpdateReviewStatus` RPC, not a batch RPC.**
"Confirm all" issues one `UpdateReviewStatus` call per photo currently in the group (sequential `await`, not `Task.WhenAll`) rather than adding a batch endpoint. Group sizes are small (a handful of photos of the same physical card), so the added round-trips are cheap, and sequential calls keep failure handling simple: if one call fails mid-way, the loop stops and the error surfaces, leaving already-confirmed photos confirmed rather than needing all-or-nothing transaction semantics. A batch RPC was considered and rejected as unnecessary complexity for this volume.

**Proto changes mirror the existing `UpdateSetName` pattern exactly**, including keeping the two `picture_service.proto` copies (`NinjagoScanner.PictureService/Protos/` and `NinjagoScanner.Web/Protos/`) in sync, matching how `UpdateSetName` was added.

**Re-fetch after every mutation, don't patch local state.**
After a series reassignment, Confirm, Has-Error, or Confirm-all action, the page reloads the full card list and recomputes groups, the same pattern `Collection.razor`'s `ReloadOverviewAsync` already uses after a sidecar edit. This guarantees the currently-shown group always reflects server truth (e.g. a reassigned photo correctly disappears from its old group) without hand-maintained client-side patch logic.

**Current group is tracked by its group key, navigation by recomputed index.**
After reloading, the page finds the current group's key (`SetName`+`CardNumber`, or the catch-all sentinel) in the freshly sorted group list to keep displaying the same group after a non-advancing action (single-photo Confirm/Has-Error/reassign). "Next"/"Previous" and "Confirm all"'s auto-advance move by index in that freshly sorted list.

**A group left empty by a reassignment auto-advances.**
If a per-photo series reassignment removes the last photo from the currently displayed group (nothing left to review there), the page advances to the next group in sort order automatically, the same way "Confirm all" advances - there is nothing left to act on so gating the user behind a manual step is exactly what "Confirm all" already avoids elsewhere on this page. If the emptied group was the last one in sort order, the page instead shows the same "nothing left to review" empty state used when no groups exist at all.

## Risks / Trade-offs

- **Re-fetching the full photo list after every action** is simpler than local patching but means a full `ListCards` round-trip per click → could feel slow for a large `cardFotos/` directory. Mitigation: `ListCards` is already the same call every other page (`/table`, `/collection`) does on load; if this proves slow in practice, a narrower "changed groups only" response would need real usage data to justify - not addressed by this change.
- **Sequential per-photo calls for "Confirm all"** means a group of N photos takes N round-trips. Mitigation: acceptable given typical group sizes (a handful of photos of one physical card); revisit as a batch RPC only if real groups turn out much larger than expected.
