## Why

The Overview page ("/") is currently just a Gemini-scan trigger button — it gives no sense of collection progress. Meanwhile the existing "web-collection-overview" capability name is easily confused with "Overview" itself, even though it actually covers the `/collection` page's per-card list/detail/edit view. Adding real at-a-glance, per-series status to "/" makes that naming collision actively confusing, so this change also renames that capability to `web-collection-list` (a pure identifier change; the page's behavior is untouched except for one small addition below).

## What Changes

- Overview ("/") gains a per-series summary, shown below the existing scan trigger: for every catalog series, the total catalog card count, the number of distinct card numbers with at least one matching photo ("owned cards"), and the total photo count (including duplicates) for that series. Category breakdown is explicitly out of scope for now.
- AI Analysis (PictureService) now writes the validated, canonical catalog series name to a photo's sidecar whenever series-name matching finds a confident match — the matching algorithm itself (exact match, then evidence-based scoring) is unchanged. When matching finds no confident match, the sidecar keeps the model's raw, unresolved guess instead of discarding it, and `AnalysisStatus` is escalated to `failed` so the mismatch is reviewable rather than silently lost.
- Series-to-photo assignment for the Overview summary is a simple exact match (after trimming whitespace and case-folding) between a photo's `Series Name` and a catalog series — reliable specifically because AI Analysis now guarantees that field is either a canonical catalog name or a deliberately non-matching raw guess (see above). Photos that don't match any series this way are counted separately as an "unknown series" bucket. This remains a separate rule from `/collection`'s existing, more lenient `Owned Copies` matching (`NormalizeSeriesKey`), which is untouched by this change. See design.md for the rationale.
- The Overview page shows the series summary as a tile/card grid — a table-layout alternative with a switch was tried during implementation and dropped after testing showed the tile grid alone looked better; see design.md.
- Selecting a series in the Overview summary navigates to `/collection` pre-filtered to that series, via a new `series` query-string parameter that `/collection` reads on load.
- **Rename**: the `web-collection-overview` capability is renamed to `web-collection-list` — no behavior change to `/collection` itself beyond the new query-string parameter above. `openspec/GLOSSARY.md`'s "Collection Overview" term should be renamed to "Collection List" as a follow-up (via `openspec-update-glossary`) once this change's specs are in place; not done as part of this change.

## Capabilities

### New Capabilities
- `web-collection-list`: renamed from `web-collection-overview` (full existing requirement set carried over unchanged) plus one new requirement: the series filter can be pre-selected via a `series` query-string parameter on page load.

### Modified Capabilities
- `web-overview`: adds the per-series summary (counts, tile/card grid, unknown-series bucket) and the click-through navigation to `/collection` with the series pre-selected.
- `web-collection-overview`: all requirements removed — superseded by `web-collection-list`.
- `picture-service-gemini-analysis`: AI Analysis writes the canonical catalog series name on a confident series-name match; when matching finds no confident match, it preserves the model's raw guess (instead of discarding it) and escalates `AnalysisStatus` to `failed`.

## Impact

- `NinjagoScanner.Web/Components/Pages/Overview.razor`: new series summary UI (tile/card grid only), click-through links.
- `NinjagoScanner.Web/Services/CardCatalogService.cs`: new aggregation method for the per-series summary; does not modify `GetCollectionOverviewAsync`/`Owned Copies`/`NormalizeSeriesKey`, which `/collection` keeps using as-is.
- `NinjagoScanner.Web/Components/Pages/Collection.razor`: new `[SupplyParameterFromQuery]` handling for a `series` parameter to pre-select the series filter on load — the first query-string-driven parameter in this Web app.
- `NinjagoScanner.PictureService/GeminiApiService.cs`: `ParseSuccessResponse`'s `SetName`/`AnalysisStatus` finalization gains a third branch for an unresolved series-name match. `SeriesCatalogService.ResolveSetName`'s matching algorithm is unchanged. No proto/gRPC contract change — `SetName` and `AnalysisStatus` already exist on `CardAnalysisResult` and the sidecar schema.
- No changes to `NinjagoScanner.CatalogService` — all needed data is already exposed via existing gRPC endpoints (`ListAllCards`, `ListCards`, `ListSeries`).
- `openspec/specs/web-collection-overview/` is removed and `openspec/specs/web-collection-list/` is created at archive time. Archived changes under `openspec/changes/archive/*/specs/web-collection-overview/` are historical snapshots and are untouched.
- `openspec/GLOSSARY.md`: follow-up needed (not part of this change) to rename the "Collection Overview" term to "Collection List" and to introduce a term for the new "unknown series" bucket.
- Not retroactive: sidecars already scanned before this change keep whatever `SetName`/`AnalysisStatus` they already have; only a re-scan picks up the new behavior. See design.md - Risks.
