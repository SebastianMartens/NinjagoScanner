## Context

See proposal.md - Why. Every series-name-based sort in the app is currently either a plain ordinal string compare (puts "Serie 10" before "Serie 2") or keyed on `Year`, which ties for same-year "Next Level" variants and therefore can't express e.g. "Serie 5" before "Serie 5 Next Level" before "Serie 6" on its own. `Year` stays in the data for display but is no longer eligible as a sort key anywhere.

The series' own detail JSON (`cardInfos/series_N[NL].json`) is the only place series-level metadata is authored today (`Jahr`, `Logo`, `Thema`, `Besonderheiten`, `Sondereditionen`), parsed by `CatalogRepository.LoadSeriesDetails`. Per-card gRPC entries (`CatalogCardEntry`) currently carry no series-level fields at all — only `series_name`, `category`, `card_number`, `card_name` — which is why the flat `ListAllCards`/collection-overview card list has nothing but the name string to sort by today.

## Goals / Non-Goals

**Goals:**
- A single, manually curated integer ordering value per series (`SortOrder`), sourced from the series' detail JSON, that every layer sorts by instead of the series name string or `Year`.
- `SortOrder` reaches per-card entries (not just per-series ones), since the card-level lists are the most visibly broken today.
- Views built from freeform/unvalidated set-name text (scanned photos) degrade gracefully when the text doesn't match a known catalog series.

**Non-Goals:**
- No validation of `SortOrder` uniqueness, gaps, or presence — confirmed with the user: the catalog is hand-maintained and trusted.
- No change to how `Year` is displayed or stored; it simply stops being read for ordering purposes.
- No automatic derivation of `SortOrder` from series name or year (e.g. regex-extracted numbers) — it is purely authored data, which is the whole point of this change.

## Decisions

**JSON field: `SortOrder` (English key), top-level per series, alongside `Jahr`/`Logo`/`Thema`.**
Confirmed with the user: new JSON keys use English now; the existing German keys (`Jahr`, `Besonderheiten`, …) get translated later as a separate effort, not bundled into this change.

**Gapped integer scheme: base series at 10, 20, 30…; "Next Level" variant at base + 5.**
Confirmed with the user. Leaves room to insert a series later (e.g. a special edition between Serie 6 and Serie 7) without renumbering existing entries.

| file | SortOrder |
|---|---|
| series_1 | 10 |
| series_2 | 20 |
| series_3 | 30 |
| series_4 | 40 |
| series_5 | 50 |
| series_5NL | 55 |
| series_6 | 60 |
| series_6NL | 65 |
| series_7 | 70 |
| series_7NL | 75 |
| series_8 | 80 |
| series_8NL | 85 |
| series_9 | 90 |
| series_9NL | 95 |
| series_10 | 100 |
| series_11 | 110 |

**`sort_order` added to `CatalogCardEntry`, not just `SeriesEntry`/`SeriesMetadata`.**
This is the field that didn't exist before at all. Without it, the flat card list (`ListAllCards`, and the Web collection overview built from it) would still have nothing but the series name string to sort by, defeating the point. Alternative considered: keep cards ordered only by joining against a separately-fetched series list client-side — rejected because it forces every card-list consumer to also fetch and join the series list just to sort, instead of getting a self-contained, pre-sorted response.

**Default when `SortOrder` is absent from a detail file: `0` (proto3 zero value), same pattern as the existing `year: 0` / empty-string defaults.**
This isn't validation (nothing is rejected or logged) — it's the same "missing optional field defaults rather than errors" convention already used for `year`, `logo`, `theme`. A series missing `SortOrder` simply sorts first, which is a visible, self-correcting symptom rather than a crash.

**Unmatched set names (Web `CardsTable` grouping) sort after all known catalog series, alphabetically among themselves.**
`CardsTable.razor` groups scanned photos by freeform `SetName` text, which may not match any catalog series (typos, unreviewed scans, genuinely new sets). These have no `SortOrder` to look up. Alternative considered: fall back to `Year`-style "sort first" (use `0`/lowest) — rejected because that would visually interleave garbage/unreviewed set names among real early series; sorting them last instead keeps the catalog-backed order clean and makes stray/bad set names easy to spot at the bottom.

**Web-side ordering is driven by the value returned over gRPC, not re-derived.**
`GetKnownSeriesAsync` (`Web/CardCatalogService.cs`) currently fetches `ListSeries` (already correctly ordered server-side) and then re-sorts it alphabetically — actively undoing the correct order. The fix removes that re-sort and orders by the now-present `sort_order` instead (or simply preserves response order, since the server already orders by `sort_order`).

## Risks / Trade-offs

- **[Risk]** Hand-entered `SortOrder` values can collide or be skipped with no error surfaced, since validation was explicitly ruled out. → **Mitigation**: none added, by request; the gapped scheme (10, 20, 30…) gives enough headroom that collisions are unlikely in practice, and a bad value's effect is limited to that one series appearing out of place, not a crash.
- **[Risk]** `Collection.razor`'s `availableSeries` list currently does `Distinct()` then `OrderBy(name)` on plain strings; once ordering comes from an external `SortOrder` value, dedup-then-order needs restructuring (e.g. `DistinctBy` name over `(name, sortOrder)` pairs, then `OrderBy(sortOrder)`) rather than a single string `OrderBy`. → **Mitigation**: called out explicitly in tasks.md so it isn't missed as "just swap the OrderBy key."
- **[Risk]** The proto change is additive (new field numbers) but still requires regenerating gRPC code and rebuilding all three projects, including `PictureService`, which never reads the new field. → **Mitigation**: purely additive/backward-compatible change; no existing field numbers or messages change shape.

## Migration Plan

1. Add `sort_order` to `SeriesEntry`, `SeriesMetadata`, and `CatalogCardEntry` in `catalog.proto`; regenerate gRPC code for all three projects.
2. Add `SortOrder` to all 16 `cardInfos/series_*.json` files per the table above.
3. Update `CatalogRepository.cs` parsing and every `OrderBy`/`ThenBy` site (`LoadSnapshot` cards, `BuildSeriesList`) to use `SortOrder` instead of series name/year; update `CardCatalogGrpcService.cs` proto mapping.
4. Update Web DTOs (`CollectionCardItem`, `CollectionCardDetails`) and `CardCatalogService.cs` (stop re-sorting `GetKnownSeriesAsync`; sort collection cards by `SortOrder`).
5. Update `Collection.razor` (availableSeries, QuickGrid "Serie" column, `GroupCardsBy`) and `CardsTable.razor` (known-series dropdown, set/series grouping with catalog lookup + fallback).
6. Update existing tests asserting the old alphabetical/year ordering (`NormalizationAndSortingTests.cs` and any gRPC service tests) and add coverage for the "Serie 10 before Serie 2" case and the NL-variant placement.

No runtime data migration is needed — this only touches static JSON files and code; rollback is a plain git revert if needed.
