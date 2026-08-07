## Why

`NinjagoScanner.CatalogService` has no automated tests today, yet it owns the most logic-heavy code in the repo: recursive JSON parsing of series detail files, card-number normalization/sorting, series-name lookup normalization, snapshot caching keyed on file timestamps, and gRPC-to-domain mapping. A follow-up change is planned to rework the card identity/data model (keying cards on `(series_name, card_number)` instead of `(series_name, category, name)` and adding multi-language card name/version support, per the TODO in `openspec/specs/catalog-service-card-catalog/spec.md`). That data-model rework will touch nearly every method in `CatalogRepository`. Doing it without a test safety net risks silently breaking sort order, deduplication, or lookup behavior that downstream consumers (`NinjagoScanner.Web`, `NinjagoScanner.PictureService`) rely on. This change adds unit test coverage now, before that rework starts.

## What Changes

- Add a new xUnit test project `NinjagoScanner.CatalogService.Tests`, referencing `NinjagoScanner.CatalogService`, and add it to `NinjagoScanner.slnx`.
- Add unit tests for `CatalogRepository` covering, via its public surface (`GetSnapshot`, `FindByName`, `FindSeriesMetadata`) driven by fixture JSON files under a temp/test data directory:
  - Series detail parsing: nested category traversal, `Karten-Nr.`/`Name` extraction, category label building, malformed/missing-file tolerance.
  - Card number normalization and sort-key ordering (numeric, `LE`-prefixed, `XXL`-prefixed, other).
  - Series/card deduplication on identical entries.
  - Exclusion of entries with blank/missing card number or name.
  - `series.json` + per-series detail file merge behavior (`BuildSeriesList`), including precedence when both sources define a series.
  - Case/whitespace/underscore/hyphen-insensitive lookup normalization for `FindByName` / `FindSeriesMetadata`.
  - Snapshot caching: unchanged directory timestamp reuses the cached snapshot; a changed file timestamp triggers a reload.
  - Fallback to an empty snapshot when loading throws (e.g. malformed `series.json`).
- Add unit tests for `CardCatalogGrpcService` covering each RPC (`ListSeries`, `GetSeries`, `ListAllCards`, `GetSeriesMetadata`, `GetServiceInfo`) against a `CatalogRepository` backed by fixture data, including not-found responses and the `IncludeKnownCardNames` flag.
- No changes to `CatalogRepository` or `CardCatalogGrpcService` production code — tests exercise the existing public API through configuration-driven fixture data directories (`IConfiguration["Catalog:Directory"]`), so no interfaces or seams need to be introduced.

## Capabilities

### New Capabilities
(none — this change adds test coverage only; it does not add or change system behavior)

### Modified Capabilities
(none — no spec-level behavior changes; see `skip_specs: true` in `.openspec.yaml`)

## Impact

- New project: `NinjagoScanner.CatalogService.Tests/` (xUnit + Moq, referencing `NinjagoScanner.CatalogService`).
- Modified: `NinjagoScanner.slnx` (adds the new test project).
- New: fixture JSON files (sample `series.json` and `series_*.json`) under the test project, mirroring the shape of files in `NinjagoScanner.CatalogService/cardInfos/`.
- No production code in `NinjagoScanner.CatalogService` changes.
- Establishes the regression safety net for the upcoming card-identity/multi-language data model change referenced in `openspec/specs/catalog-service-card-catalog/spec.md`.
