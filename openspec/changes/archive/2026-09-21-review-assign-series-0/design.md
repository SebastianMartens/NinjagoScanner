## Context

The review page fills its per-photo series grid from `CatalogServiceClient.GetKnownSeriesAsync`, i.e. `ListSeries`, so every catalog series already gets a button (text-only when it has no logo mapping). Series 0 has no button because of the data, not the page: `series_0_spinner.json` declares its series as `Serie_1`. `CatalogRepository.LoadSeriesDetails` stores series in a dictionary keyed by normalized name (`result[seriesKey] = ...`), so the later file enumerated wins. The checked-in catalog snapshot (200 `Serie 1` cards, no `Beispiel` card) shows Series 1 currently wins and Series 0 is lost. See proposal.md - Why.

Loading errors that violate the data contract already surface as `CatalogDataException` (e.g. a card with no `Class`), rethrown with the file name and not swallowed by `LoadSnapshot`.

## Goals / Non-Goals

**Goals:**
- "Serie 0" exists as its own catalog series, first in the series order, with a button on the review page.
- A repeat of this mistake (two files claiming the same series) cannot pass unnoticed.

**Non-Goals:**
- A Series 0 logo, or changing how review groups are built.
- Changing the picker markup, the gRPC contract, or PictureService.

## Decisions

**Fix the data key, leave the page alone.** Renaming `Serie_1` to `Serie_0` is sufficient for the button to appear, because the grid is data-driven. Alternative: hard-code a "Serie 0" button in `Review.razor`. Rejected: it would duplicate the catalog, still leave Series 1 clobbered in the catalog, and break the rule that the grid lists exactly the catalog's series.

**Reject duplicate series names at load time.** In `LoadSeriesDetails`, if `result` already contains the normalized key, throw `CatalogDataException` naming the series and file. Alternatives: keep last-wins (the status quo that caused this), or merge cards of same-named series. Merging was rejected because there is no case for one series split across files, and it would hide typos like this one. Cost: a bad data file now takes the catalog down instead of silently degrading, which is the same failure mode as the existing `Class` validation, and the shipped-data tests catch it before deploy.

**Sort order 0 gives "first" for free.** `ListSeries` already orders by `SortOrder`, and Series 1 is 10, so no ordering change is needed; the tests just pin it.

**Series 0 ships with its real cards, numbered to stay unique.** The Wave 1 and Wave 2 lists both start at 1, but review grouping keys cards by series + card number (`BuildReviewGroups` uses `TryAdd`, so a duplicate would be silently shadowed). Wave 1 keeps its printed numbers (1-81), Wave 2 gets the prefix `WB` (`WB1`-`WB125`). Limited editions use `LE` (Wave 1) and `WBLE` (Wave 2) like the other series; the source's `★` cannot be used because `NormalizeCardNumber` strips non-alphanumerics (`★1` would become `1`). The prefix is letters only: a digit in it (e.g. `W2-`) would normalize to `W21...` and `ToSortKey` (`^[A-Z]+\d+$`) would sort it wrongly. Waves are separated by category ("Deck 1" / "Deck 2"), not by series, so there is still one "Serie 0" button. Alternatives considered: a separate series file for Wave 2 (extra button, extra sort order) and identical numbers in one series (Wave 2 cards 1-81 would be shadowed). The source list numbers the last Wave 2 special card "★22 Rattla"; that is a typo for ★6, so it is `WBLE6`.

**Snapshot baseline is regenerated, not hand-edited.** `Fixtures/ShippedCatalogCards.snapshot.tsv` gains the 221 `Serie 0` cards; regenerate with `UPDATE_CATALOG_SNAPSHOT=1` and review that the diff is only added `Serie 0` lines (nothing removed or changed).

**Test the grid's data source through the real gRPC path.** The Razor page has no unit-testable seam beyond the client, so the Web test asserts `CatalogServiceClient.GetKnownSeriesAsync` against `CatalogServiceTestHost` with `Serie_0` and `Serie_1` files, expecting "Serie 0" then "Serie 1".

## Risks / Trade-offs

- [Wave 2 photos only join a catalog card group if their sidecar card number is the `WB`-prefixed form; a photo carrying the plain printed number (e.g. `1`) matches the Wave 1 card instead] → Accepted; the review page's card-number correction lets the user set the right one, and the AI analysis has no notion of waves. `SetName` is saved either way.
- [The duplicate guard makes a mistaken data file fatal for the whole catalog] → Same as existing data-contract errors; caught by the shipped-catalog tests in CI before deploy.
- [Existing stored sidecars with `SetName` "Serie 1" for what are really Series 0 cards] → Not migrated here; the user reassigns them via the new button.
