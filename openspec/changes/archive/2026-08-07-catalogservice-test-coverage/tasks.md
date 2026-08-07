## 1. Project Setup

- [x] 1.1 Create `NinjagoScanner.CatalogService.Tests` project (xUnit, `net10.0`), add `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `Moq` package references
- [x] 1.2 Add a project reference from `NinjagoScanner.CatalogService.Tests` to `NinjagoScanner.CatalogService`
- [x] 1.3 Add `NinjagoScanner.CatalogService.Tests` to `NinjagoScanner.slnx`
- [x] 1.4 Add a shared test fixture helper (e.g. `TempCatalogDirectory`) that creates a temp directory, writes given `series.json`/`series_*.json` content into it, exposes an `IConfiguration` pointing `Catalog:Directory` at it, and deletes the directory on dispose

## 2. CatalogRepository - Parsing and Extraction

- [x] 2.1 Test `GetSnapshot()` parses a nested `series_*.json` detail file into cards, correctly building category labels from nested object paths (`EnumerateCardEntries` / `BuildCategoryLabel`)
- [x] 2.2 Test cards missing `Karten-Nr.` or `Name`, or with a blank/whitespace value for either, are excluded from `Cards`
- [x] 2.3 Test category name normalization (underscore-to-space, whitespace collapsing) via `ToCategoryDisplayName`, and that reserved keys (`Jahr`, `Logo`, `Thema`, `Besonderheiten`, `Kategorien`, `Serie*`) are not treated as categories
- [x] 2.4 Test a malformed/non-JSON `series_*.json` detail file is skipped without throwing, while other valid detail files still load
- [x] 2.5 Test series metadata extraction (`Jahr`, `Logo`, `Thema`, `Besonderheiten`) from a detail file, including defaults when fields are absent or the wrong JSON kind

## 3. CatalogRepository - Normalization and Sorting

- [x] 3.1 Test `NormalizeCardNumber` behavior: numeric strings collapse to their integer form, non-alphanumeric characters are stripped, casing is normalized to upper
- [x] 3.2 Test card sort order via `GetSnapshot().Cards`: purely numeric card numbers sort first (numerically), then `LE`-prefixed, then `XXL`-prefixed, then other formats, with series/category/name as tiebreakers
- [x] 3.3 Test `FindByName` / `FindSeriesMetadata` normalize lookups so that case, underscores, hyphens, and extra whitespace differences all match the same series

## 4. CatalogRepository - Dedup, Merge, and Caching

- [x] 4.1 Test that two identical card entries (same series, category, normalized card number, and name) collapse into a single `Cards` entry
- [x] 4.2 Test `BuildSeriesList` merge behavior: a series present only in `series.json`, only in a detail file, and in both (verifying field precedence) all produce correct `SeriesCatalogItem` entries
- [x] 4.3 Test `GetSnapshot()` returns the cached snapshot instance when no file under the data directory has changed
- [x] 4.4 Test `GetSnapshot()` reloads when a file's last-write timestamp changes (using `File.SetLastWriteTimeUtc`, not a sleep)
- [x] 4.5 Test `GetSnapshot()` falls back to an empty snapshot (empty `Series`/`Cards`, non-null `DataDirectory`) when `series.json` is malformed and loading throws

## 5. CardCatalogGrpcService

- [x] 5.1 Test `ListSeries` maps every series from the snapshot into the response, honoring `IncludeKnownCardNames` (present when true, absent/empty when false)
- [x] 5.2 Test `GetSeries` returns `Found = true` with the mapped series for an existing (including differently-cased/formatted) name, and `Found = false` for an unknown name
- [x] 5.3 Test `ListAllCards` maps every card in the snapshot to a `CatalogCardEntry` with matching fields
- [x] 5.4 Test `GetSeriesMetadata` returns `Found = true` with mapped metadata (including zero-value defaults for missing `Year`/`Logo`/`Theme`) for a known series, and `Found = false` for an unknown one
- [x] 5.5 Test `GetServiceInfo` returns the snapshot's `DataDirectory`, `Series.Count`, and an ISO-8601 (`"O"`-formatted) `LoadedAtUtc`

## 6. Verification

- [x] 6.1 Run `dotnet test` for `NinjagoScanner.CatalogService.Tests` and confirm all tests pass
- [x] 6.2 Run `dotnet build` on `NinjagoScanner.slnx` to confirm the new test project doesn't break the solution build
