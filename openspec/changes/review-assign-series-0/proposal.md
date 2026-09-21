## Why

A new "Series 0" (very early 2011 cards sold with Ninjago Spinner sets) was added to the catalog in `series_0_spinner.json`, and the user needs to assign photographed cards to it from the review page. Today that cannot work: the file declares its series under the key `Serie_1`, the same key as the real Series 1, so the catalog loader (which keys series by normalized name) lets one of the two silently overwrite the other. Series 0 never shows up as its own series, and Series 1 may lose its cards depending on file enumeration order.

## What Changes

- Rename the series key in `NinjagoScanner.CatalogService/cardInfos/series_0_spinner.json` from `Serie_1` to `Serie_0`, so the catalog exposes a distinct series named "Serie 0" (sort order 0, i.e. listed before Series 1).
- The review page's series-assignment grid already renders one button per catalog series, so "Serie 0" gets its button there with no page change. It has no logo mapping, so it renders as a text-only cell like Series 1's fallback.
- Make the catalog loader fail loudly when two series detail entries resolve to the same normalized series name, instead of silently overwriting one. This guards against the exact mistake that hid Series 0.
- Fill `series_0_spinner.json` with the real Series 0 cards (221, from the Ninjago Spinners card list): Wave 1 (2011) as "Deck 1" categories with plain numbers 1-81 plus limited editions `LE1`-`LE9`, and Wave 2 (2012) as "Deck 2" categories. Wave 2 also numbers from 1, so its card numbers carry the prefix `WB` (`WB1`-`WB125`, limited editions `WBLE1`-`WBLE5` and `WBLE22`) to stay unique within the series; the prefix is letters only because the loader strips punctuation and sorts letter-prefix + digits.
- Add tests: the catalog exposes "Serie 0" and "Serie 1" as separate series; a duplicate series name is rejected; Series 0 card numbers are unique; the review page's series grid includes "Serie 0" before "Serie 1".

Out of scope: a Series 0 logo, and any change to how the review page groups photos.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `catalog-service-series-catalog`: series names must be unique across detail files (a duplicate is a data error, not a silent overwrite), and a series with `SortOrder` 0 is listed first.
- `web-card-review-flow`: the series-reassignment grid must offer every catalog series, including Series 0, in catalog order.

## Impact

- `NinjagoScanner.CatalogService/cardInfos/series_0_spinner.json` (key rename, plus the 221 real cards).
- `NinjagoScanner.CatalogService.Tests/Fixtures/ShippedCatalogCards.snapshot.tsv` (baseline regenerated: 221 added Series 0 lines).
- `NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs` (`LoadSeriesDetails` duplicate detection, throwing `CatalogDataException`).
- Tests in `NinjagoScanner.Web.Tests` (CatalogService is exercised through its in-process test host there) and the review page series grid.
- No `.proto` change, no PictureService change, no Web UI markup change.
- Photos assigned to "Serie 0" resolve to a catalog card group when their card number matches a Series 0 catalog card (Wave 1 plain numbers, Wave 2 `WB`-prefixed). Wave 2 photos only match if their sidecar card number uses the `WB` form; otherwise they stay in the "Ohne bekannte Serie" group with their `SetName` saved.
