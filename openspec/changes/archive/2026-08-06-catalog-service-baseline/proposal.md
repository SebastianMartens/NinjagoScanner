## Why

NinjagoScanner.CatalogService is already running in production (it backs PictureService's card lookups and will back the Web catalog views) but has no OpenSpec specs. Its gRPC contract and data-loading behavior currently exist only as source code, making it hard to propose or review future changes against a documented baseline. This change establishes that baseline: no source code changes, only specs describing current behavior.

## What Changes

- Add baseline specs for the `CardCatalog` gRPC service (`Protos/catalog.proto`) covering all five RPCs: `ListSeries`, `GetSeries`, `ListAllCards`, `GetSeriesMetadata`, `GetServiceInfo`.
- Add a baseline spec for the catalog data-loading and cache-refresh behavior implemented in `CatalogRepository` (merging `series.json` with per-series `series_*.json` detail files, and reloading when files on disk change).
- No behavior, API, or code changes — this is a documentation-only baseline.

## Capabilities

### New Capabilities
- `catalog-service-series-catalog`: `ListSeries` / `GetSeries` RPCs — list and look up series entries (year, special features, special editions, optionally known card names), built by merging the main `series.json` catalog with per-series detail files.
- `catalog-service-card-catalog`: `ListAllCards` RPC — flattened, deduplicated, sorted list of every card across all series (series name, category, card number, card name).
- `catalog-service-series-metadata`: `GetSeriesMetadata` RPC — per-series descriptive metadata (year, logo, theme, highlights) looked up by series name.
- `catalog-service-service-info`: `GetServiceInfo` RPC — diagnostics endpoint reporting the resolved data directory, loaded series count, and last load timestamp.
- `catalog-service-catalog-refresh`: file-timestamp-based cache invalidation — the in-memory catalog snapshot is rebuilt from disk whenever any file under the data directory changes more recently than the cached snapshot, without requiring a service restart.

### Modified Capabilities
(none — first specs for this project)

## Impact

- Adds files under `openspec/specs/catalog-service-*/spec.md` only; no changes to `NinjagoScanner.CatalogService` source.
- Establishes the documented contract that `NinjagoScanner.PictureService` (via `CatalogGrpcClient`) and, in future, `NinjagoScanner.Web` depend on when consuming catalog data over gRPC.
