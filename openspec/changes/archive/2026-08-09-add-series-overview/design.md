## Context

See proposal.md - Why. Relevant current state:

- `CardCatalogService.GetCollectionOverviewAsync()` (`NinjagoScanner.Web/Services/CardCatalogService.cs`) already merges CatalogService's full card list with PictureService's scanned photo sidecars into `CollectionCardItem` entries (`Series`, `SortOrder`, `Category`, `CardNumber`, `OwnedCopies`), used by `/collection`. `OwnedCopies` is computed via `BuildOwnershipKey`/`NormalizeSeriesKey`, which aggressively normalizes series names (strips all whitespace/dashes/underscores, collapses "Next Level" to "NL" via regex) before comparing.
- No new gRPC calls are needed anywhere in this change — `ListAllCards` (CatalogService) and `ListCards` (PictureService) already return everything required.
- Series JSON catalog files (`NinjagoScanner.CatalogService/cardInfos/series_*.json`) carry `Logo` as descriptive text ("Kein Logo"), not an image — no real artwork is available for tiles.
- No page in this Web app currently reads query-string parameters (`[SupplyParameterFromQuery]` is unused today).
- `NinjagoScanner.PictureService/SeriesCatalogService.ResolveSetName` already implements exact-then-evidence-based series-name matching (capability `picture-service-series-name-matching`, unchanged by this design). `GeminiApiService.ParseSuccessResponse` currently finalizes the result with:
  ```csharp
  var normalizedStatus = NormalizeStatus(payload.Status, payload.Confidence);
  var resolvedSetName = SeriesCatalogService.ResolveSetName(payload, seriesCatalog);
  SetName = normalizedStatus == AnalysisStatuses.Failed ? null : resolvedSetName;
  ```
  i.e. today, whenever `resolvedSetName` is null (no confident match) — regardless of status — `SetName` ends up null either way, and the model's original guess is discarded (only recoverable from the sidecar's preserved `RawModelResponse` diagnostic field, not from `SetName` itself).

## Goals / Non-Goals

**Goals:**
- Give the Overview page real, per-series collection status without adding new backend/gRPC surface.
- Keep the Web-layer series-matching rule fully isolated from `/collection`'s existing `Owned Copies` computation.
- Make an unresolved series-name match a first-class, reviewable outcome of AI Analysis (raw guess preserved, status escalated to `failed`) rather than a value silently discarded at analysis time.

**Non-Goals:**
- Category breakdown within a series (deferred).
- Reconciling or unifying the new strict matching rule with `/collection`'s existing lenient matching — they are allowed to diverge (see Decisions).
- Series Metadata (year/theme/logo) on the summary.
- Making the unknown-series bucket clickable/navigable (there's no catalog series to link it to).

## Decisions

### Series-name resolution happens once, at AI Analysis time — not in the Web layer
Rather than having the Overview feature invent its own data-cleanup logic, `GeminiApiService.ParseSuccessResponse` (PictureService) is responsible for producing a trustworthy `SetName`: the canonical catalog name on a confident match, or a deliberately non-matching raw guess (plus an escalated `failed` status) otherwise. The Web-layer per-series summary then only needs a simple trim + case-fold equality check against known catalog series names — it is not doing any fuzzy cleanup of its own, just bucketing already-clean-or-deliberately-dirty data. Both sub-metrics on a series tile (owned-card count and total photo count) derive from this same equality check, so the two numbers on one tile always reconcile with each other.

**Alternative considered**: keep series-name cleanup entirely in the Web layer (an earlier version of this design used a standalone strict-matching helper there, independent of PictureService). Rejected per explicit product direction: matching belongs where the existing fuzzy exact-then-evidence-based algorithm already lives (`SeriesCatalogService`, capability `picture-service-series-name-matching`), and pushing the decision upstream means every consumer of a sidecar's `SetName` (not just this new Overview feature) benefits from a validated value or an intentionally-flagged failure.

**Alternative considered (for the escalation itself)**: leave `AnalysisStatus` as `ok`/`uncertain` when series-name matching fails, and rely solely on the Overview page's name-mismatch to surface it as "unknown series." Rejected per explicit user request: an unresolved series is itself a failure worth surfacing everywhere `AnalysisStatus` is shown (e.g. the `/review` flow), not just as an absence on one summary page.

**Consequence worth flagging**: `/collection`'s `Owned Copies` (`NormalizeSeriesKey`/`BuildOwnershipKey`) remains untouched and more lenient than the exact-match rule above — for *already-scanned* photos whose `SetName` predates this change (or was set manually with inconsistent formatting), the two pages can still disagree about which series a photo belongs to. This is mitigated for all newly- and re-scanned photos, where `SetName` is now guaranteed to be either fully canonical or a clearly non-matching raw guess.

### `ParseSuccessResponse` gains a third branch for the finalization ternary
The existing two-way `SetName = normalizedStatus == Failed ? null : resolvedSetName` becomes three-way:
- `normalizedStatus == Failed` (model itself reported failure) → `SetName = null`, unchanged from today — the card data is unreliable altogether, not just the series.
- `resolvedSetName is not null` (confident match) → `SetName = resolvedSetName`, unchanged from today.
- otherwise (status was `ok`/`uncertain` but `resolvedSetName` is null) → **new**: `AnalysisStatus` is escalated to `failed`, and `SetName` is set to the model's own raw/trimmed set-name guess instead of `null`.

This keeps `SeriesCatalogService.ResolveSetName` itself untouched — only how `GeminiApiService` uses its result changes.

### New aggregation method, computed in `CardCatalogService`
A new method (analogous in shape to `GetCollectionOverviewAsync`) loads the catalog's card list and the scanned photo entries, groups catalog cards by series (`SortOrder` ascending, per existing convention), and for each series counts total cards, distinct owned card numbers, and total matching photos using the strict-match rule above. Photos whose Series Name doesn't exactly match (trim/case-fold) any catalog series name are aggregated into a single "unknown series" total, not attributed to any entry.

### Tile/card grid only — no layout switch
The first iteration of this change built a tile-vs-table layout toggle (a simple in-memory field in `Overview.razor`, mirroring `Collection.razor`'s unpersisted `groupBy`/filter state). After trying both in the running app, the table view looked worse than the tile grid — the toggle added a control and a whole second rendering path for a layout nobody preferred. Removed; the summary is tiles only, unconditionally. Noted here so a future contributor doesn't re-propose the same toggle without knowing it was already tried and dropped.

### `/collection` reads `series` via `[SupplyParameterFromQuery]`
`Collection.razor` is `@page "/collection"`; adding a `[SupplyParameterFromQuery] public string? Series { get; set; }` property and applying it to `SelectedSeries` during `OnInitializedAsync` (after `availableSeries` is loaded, so an unrecognized value can be safely ignored) is the standard Blazor pattern and requires no routing changes.

## Risks / Trade-offs

- **[Risk]** Two different "which series does this photo belong to" answers in the same app (exact-match on Overview, lenient `NormalizeSeriesKey` on `/collection`) could confuse a user comparing the two pages, for photos whose `SetName` isn't yet canonical. → **Mitigation**: substantially reduced by this design — every newly- or re-scanned photo now gets a canonical `SetName` on a confident match, so the two pages agree in the common case. Remaining divergence is limited to photos scanned before this change and never re-scanned; documented as intentional and revisited only if it causes real confusion.
- **[Risk]** Not retroactive: sidecars scanned before this change already have whatever `SetName`/`AnalysisStatus` the old two-way logic produced — including cases where an unresolved series was already silently written as `null` and the model's raw guess is gone from `SetName` (though it may still be recoverable from the sidecar's `RawModelResponse` diagnostic field). → **Mitigation**: re-running AI Analysis on a photo is already a supported action in this app; no separate migration task is proposed here.
- **[Risk]** A photo with a slightly-off `Series Name` (extra internal whitespace variant, different case than expected) that somehow bypasses AI Analysis's matching (e.g. a manual sidecar edit typo) silently lands in "unknown series" instead of a specific series tile. → **Mitigation**: the unknown-series bucket is always shown when its count is greater than zero, consistent with how `/collection` already omits its "nicht zugeordnet" stat at zero, so the discrepancy is visible rather than silent.

## Open Questions

None — the questions raised during exploration (matching scope, Next Level handling, layout persistence, tile content) were resolved before writing this design.
