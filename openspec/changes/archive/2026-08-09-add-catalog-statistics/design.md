## Context

`CardCatalogService.GetSeriesSummaryAsync()` (`NinjagoScanner.Web/Services/CardCatalogService.cs`) already loads every catalog card (via CatalogService's `ListAllCards`) and every scanned photo (via PictureService's `ListCards`, which returns `CardEntry` with `AnalysisStatus` and `ReviewStatus` already populated), and groups them per series using an exact-match rule (trim + case-fold `SetName` against series name) to compute each series's `OwnedCards`/`TotalCards`/`TotalPhotos`. See proposal.md for why a catalog-wide rollup of that same data is now wanted.

## Goals / Non-Goals

**Goals:**
- Add one new aggregation to `CardCatalogService` that produces catalog-wide totals: total catalog cards, owned catalog cards, total photos, analysis-status counts, review-status counts.
- Reuse the existing per-series ownership matching (exact-match, trim + case-fold) so the catalog-wide "owned cards" figure is arithmetically consistent with summing the per-series tiles.
- Render the result as a plain stats section on the Overview page, consistent with the app's existing text/number stat conventions (no charting library).

**Non-Goals:**
- Changing `/collection`'s lenient ownership matching (`NormalizeSeriesKey`) — untouched.
- Historical/time-series statistics (trends over time) — only current-state counts.
- Per-series breakdown of analysis/review status — that stays catalog-wide only, per the proposal; per-series drill-down can be a future change if wanted.

## Decisions

- **Reuse `GetSeriesSummaryAsync`'s per-series computation rather than a separate simpler catalog-wide join.** Computing "owned catalog cards" as a single flat join (all photos vs. all cards, normalized) would be cheaper but could drift from the per-series tiles' definition of "owned" if the two rules ever diverge. Summing the already-computed per-series `OwnedCards` (or extending the same method to also return a catalog-wide rollup) guarantees the numbers agree by construction. Concretely: extend `GetSeriesSummaryAsync` to also return the catalog-wide totals alongside the existing per-series list, in the same `SeriesSummaryResult`, rather than adding a second gRPC round-trip via a new method.
- **Analysis-status and review-status counts are computed over *all* scanned photos, not just series-matched ones.** These are per-photo properties independent of catalog matching; scoping them to "matched only" would hide photos with an unrecognized series (already surfaced elsewhere as the "unknown series" count) from the health breakdown, which is misleading for someone checking overall scan/review progress.
- **No new gRPC contract.** `CardEntry.AnalysisStatus`/`CardEntry.ReviewStatus` are already on the wire; the aggregation is pure in-memory LINQ over data already being fetched for the series summary, so it costs one more pass over the same in-memory list.
- **UI: plain stat row, no charting library**, matching the rest of the app (`/collection`'s inline stats bar, the series tile's stats row). Consistent look, no new dependency.

## Risks / Trade-offs

- [Analysis/review counts include photos with an unresolved series name, unlike the per-series tiles] → Documented explicitly in the spec scenarios; the "owned cards" figure and the status breakdown are answering different questions (catalog coverage vs. photo health) and are allowed to use different denominators.
- [Extending `SeriesSummaryResult`'s shape changes an existing model consumed by `Overview.razor`] → Additive only (new fields on the existing result), so the current per-series tile rendering is unaffected.
