## 1. Catalog data and loader

- [x] 1.1 Rename the series key in `NinjagoScanner.CatalogService/cardInfos/series_0_spinner.json` from `Serie_1` to `Serie_0`; verify with `dotnet test NinjagoScanner.CatalogService.Tests` (task 1.4) that the catalog lists "Serie 0" and "Serie 1" separately.
- [x] 1.2 In `CatalogRepository.LoadSeriesDetails`, throw `CatalogDataException` (naming the series and file) when a series' normalized key is already loaded, instead of overwriting; verify with the duplicate-series test in 1.3.
- [x] 1.3 Add `CatalogRepositoryTests` using `TempCatalogDirectory`: (a) two files declaring `Serie_1` and `Serie 1` fail loading with `CatalogDataException`; (b) files declaring `Serie_0` and `Serie_1` list both, "Serie 0" first, each with its own cards. Verify both pass.
- [x] 1.4 Fill `series_0_spinner.json` with the 221 real cards: Wave 1 (17 character, 64 battle numbered 18-81, 9 `LE1`-`LE9`) under "... Deck 1" categories and Wave 2 (25 character, 100 battle, 6 special) as `WB1`-`WB125`, `WBLE1`-`WBLE6` under "... Deck 2" categories; verify the JSON has no duplicate card numbers and the catalog service builds.
- [x] 1.5 Regenerate `Fixtures/ShippedCatalogCards.snapshot.tsv` with `UPDATE_CATALOG_SNAPSHOT=1 dotnet test NinjagoScanner.CatalogService.Tests`, confirm `git diff` shows only 221 added `Serie 0` lines (no removed or changed lines) and Series 1 still has its 200 cards, then rerun without the variable and verify it passes.
- [x] 1.6 Add a shipped-data test asserting `ListSeries`/snapshot contains "Serie 0" before "Serie 1", that "Serie 0" has 221 cards with no duplicate card numbers, and that Series 1 keeps its cards; verify it passes.

## 2. Review page series grid

- [x] 2.1 Add a Web test in `NinjagoScanner.Web.Tests/Services` that writes `Serie_0` and `Serie_1` files into `CatalogServiceTestHost` and asserts `CatalogServiceClient.GetKnownSeriesAsync` returns "Serie 0" then "Serie 1"; verify it passes.
- [ ] 2.2 Run the app (`Launch All`), open `/review`, and confirm a text-only "Serie 0" button appears first in a photo tile's series grid, that clicking it saves `SetName` "Serie 0" without changing `ReviewStatus`, and that the photo then moves to its Series 0 card group when its card number matches (e.g. `5`), or stays in "Ohne bekannte Serie" when it doesn't. No `Review.razor` change is expected; if the button is missing, investigate before adding markup.

## 3. Final checks

- [x] 3.1 Run `dotnet test NinjagoScanner.slnx` and verify everything passes.
