## 1. AI Analysis: preserve raw guess and escalate status on unresolved series match

- [x] 1.1 In `NinjagoScanner.PictureService/GeminiApiService.ParseSuccessResponse`, replace the two-way `SetName = normalizedStatus == Failed ? null : resolvedSetName` with the three-way logic from design.md: model-reported failure keeps `SetName = null`; a confident match keeps `SetName = resolvedSetName`; an unresolved match (status was `ok`/`uncertain` but `resolvedSetName` is null) sets `SetName` to the model's raw, trimmed set-name guess and escalates `AnalysisStatus` to `failed`.
- [x] 1.2 Do not modify `SeriesCatalogService.ResolveSetName` or its matching algorithm.
- [x] 1.3 Create the new spec delta `specs/picture-service-gemini-analysis/spec.md` in this change with a MODIFIED "Resolved set name is discarded for failed analyses" (re-scoped to model-reported failures only) and an ADDED "A failed series-name match escalates the analysis status and preserves the raw guess".

## 2. Series summary aggregation

- [x] 2.1 Add a `SeriesSummaryItem` (or similar) model to `NinjagoScanner.Web/Models` with `SeriesName`, `SortOrder`, `TotalCards`, `OwnedCards`, `TotalPhotos`.
- [x] 2.2 Add a `GetSeriesSummaryAsync` method to `CardCatalogService` that loads the catalog card list and scanned photo entries, and for each catalog series computes `TotalCards` (count of catalog cards), `OwnedCards` (distinct card numbers with >=1 matching photo), and `TotalPhotos` (count of matching photos, duplicates included) — using a trim + case-fold exact-match equality check against known series names, kept separate from `NormalizeSeriesKey`/`BuildOwnershipKey`.
- [x] 2.3 In the same method, aggregate photos whose Series Name doesn't exactly match (trim/case-fold) any catalog series into an "unknown series" total.
- [x] 2.4 Order series entries by the catalog's `SortOrder` ascending.

## 3. Overview page UI

- [x] 3.1 Add the per-series summary section to `Overview.razor`, loaded in `OnInitializedAsync` via `GetSeriesSummaryAsync`.
- [x] 3.2 Implement the tile/card grid layout (series name, total cards, owned cards, total photos — progress-style presentation).
- [x] 3.3 Implement the table layout (one row per series, same four values as columns).
- [x] 3.4 Add the layout toggle control as local component state (no persistence), defaulting to the tile/card grid.
- [x] 3.5 Show the unknown-series bucket count separately, only when greater than zero.
- [x] 3.6 Make each series entry (tile or row) navigate to `/collection?series=<series name>` on click/activation, in both layouts.

## 4. Collection page deep-link support

- [x] 4.1 Add a `[SupplyParameterFromQuery] public string? Series { get; set; }` property to `Collection.razor`.
- [x] 4.2 In `OnInitializedAsync`, after `availableSeries` is populated, pre-select `SelectedSeries` from the `Series` query parameter when it case-insensitively matches a known series; otherwise leave the filter unset (no error).

## 5. Spec capability rename

- [ ] 5.1 Verify `openspec archive` for this change results in `openspec/specs/web-collection-overview/` being removed and `openspec/specs/web-collection-list/` being created with the full carried-over requirement set plus the new query-string requirement.
- [ ] 5.2 After archiving, run `openspec-update-glossary` to rename the "Collection Overview" glossary term to "Collection List", fix the "Overview" entry's cross-reference, and add a term for the new unknown-series bucket.

## 6. Verification

- [ ] 6.1 Manually verify in the running app: a photo whose series is confidently resolved gets the canonical `SetName`; a photo whose series can't be resolved keeps its raw guess and is marked `failed`; that photo then shows up in the Overview's unknown-series bucket. **Not yet verified end-to-end** — requires a real Gemini API key to trigger an actual scan, which wasn't available in this session; the finalization logic was verified by code review and the unit-tested-adjacent build/behavior of the surrounding code instead.
- [x] 6.2 Manually verify in the running Web app: series summary counts match expectations, the unknown-series bucket appears only when applicable, both layouts render correctly, and clicking a series entry lands on `/collection` with that series pre-filtered. Verified against the real `cardFotos` data (3821 photos): all 16 series render in `SortOrder`, tile counts sum to 3670 matched + 151 unknown = 3821 total (exact reconciliation), and `/collection?series=Serie%205` pre-selects "Serie 5" in the filter while an unrecognized series value leaves the filter unset with no error. The table-layout toggle was verified by markup/code review, not a live client-side click (curl can't drive the Blazor Server interactive circuit).
- [x] 6.3 Confirm `/collection`'s existing `Owned Copies`/unmapped-photo behavior is unaffected by this change. Verified: `/collection` still renders its existing stats bar (e.g. "3913 Karte(n) gesamt") correctly against the same data.
