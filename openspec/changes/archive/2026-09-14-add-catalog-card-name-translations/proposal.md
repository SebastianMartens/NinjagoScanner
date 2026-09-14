## Why

Catalog card names in `cardInfos/*.json` are currently stored as a single string, and that string is a mix of languages depending on which series file it came from (some series were transcribed from German source material, others from English). There is no way to know, or later add, a card's name in more than one language, even though the domain already treats card name as descriptive rather than identifying (see `catalog-service-card-catalog`'s resolved note: "translations give it multiple values per card"). This change lays the groundwork — schema and resolution logic only — so future work (a user-facing language preference, AI-assisted enrichment) has real per-language data to build on.

## What Changes

- `cardInfos/*.json`: `Name` on each card entry changes from a plain string to a per-language object, e.g. `{"de": "Kai", "en": "Kai"}`. **BREAKING** for the on-disk JSON shape (not for any API/contract).
- One-time data migration of all 16 existing `series_*.json` files: wrap each card's current `Name` string under the language it was actually sourced in.
  - `series_3.json` and `series_8NL.json`: existing names are German → become `{"de": "<existing>"}`.
  - All other series files (`series_1`, `2`, `4`, `5`, `5NL`, `6`, `6NL`, `7`, `7NL`, `8`, `9`, `9NL`, `10`, `11`): existing names are English → become `{"en": "<existing>"}`.
- `CatalogRepository` parses the new per-language `Name` object and resolves it to a single display name: German if present, otherwise English. This keeps `CatalogCardItem.CardName` (and therefore the gRPC contract and every downstream consumer in PictureService/Web) unchanged as a single required string — only the internal parsing step changes.
- No gRPC contract change, no user-facing language preference (still implicitly German-first), no AI-assisted enrichment. Those are explicitly out of scope for this change.

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `catalog-service-card-catalog`: card name resolution now reads from a per-language map in the source JSON (German preferred, English fallback) instead of a single plain string, and an entry with neither language populated is treated as missing a card name.

## Impact

- **NinjagoScanner.CatalogService**: `Catalog/CatalogRepository.cs` (`ExtractSeriesCards`/`EnumerateCardEntries` — parsing of the `Name` property and the new de→en resolution), `cardInfos/*.json` (16 files, data migration).
- **NinjagoScanner.CatalogService.Tests**: parsing/fixture tests that assert on `Name` shape or resolved `CardName` values.
- No changes to `Protos/catalog.proto`, `CatalogContracts.cs`, PictureService, or Web — `CatalogCardItem.CardName` keeps its current single-string shape and meaning.
