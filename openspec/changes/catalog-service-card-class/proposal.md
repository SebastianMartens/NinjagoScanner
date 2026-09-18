## Why

PictureService's card-analysis pipeline is being split into staged steps (see the companion
`picture-service-staged-analysis-pipeline` change), one of which needs to classify a scanned
photo into a small, fixed, cross-series physical/depiction class (character, action, vehicle,
puzzle-piece, trap, limited edition, art) so catalog matching can narrow candidates within a
series. The catalog already has a `Category` concept (e.g. `Heroes`, `Puzzle_Cards`,
`Mega_Villains_Cards`) but it is fine-grained, per-series, and inconsistent across the 16 series
files (~33 distinct strings, e.g. `Heroes` in later series vs. `Good_Guys` in `Serie_1`/`Serie_2`
for the same role) - not something a classification step can target directly. This change adds a
coarser `Class` alongside the existing `Category`, owned by CatalogService since it is catalog
data, not scan-time behavior.

## What Changes

- Every category entry in every `NinjagoScanner.CatalogService/cardInfos/*.json` file is
  reshaped from a flat card array to an object carrying a `Class` alongside the cards, e.g.
  `"Heroes": [ {...} ]` becomes `"Heroes": { "Class": "character", "Karten": [ {...} ] }`.
  **BREAKING** (internal data format): every `cardInfos/*.json` file changes shape; the JSON
  parser in `CatalogRepository.cs` must be updated in the same change or it will silently
  misparse category labels (see Impact).
- A fixed set of 7 classes for now: `character`, `action`, `vehicle`, `puzzle-piece`, `trap`,
  `limited edition`, `art`. Every one of the ~33 existing category strings is force-fit into one
  of these today (may grow later) - see design.md for the full mapping and the judgment calls it
  required for format/rarity-flavored categories (`Mega_*`, `XXL_*`, `Ultra_*`, `Platinum_Cards`,
  etc.) that don't map cleanly to a depiction-based class.
- `Class` is assigned per category, inline, separately in each series file - not via one shared
  lookup table - even though the same category name (e.g. `Heroes`, `Villains`,
  `Limited_Edition_Cards`) recurs across most of the 16 files. This trades a small risk of
  cross-file drift for simplicity; a regression test (see below) guards against that drift.
- `CatalogCardItem` (`CatalogContracts.cs`) and the `CatalogCardEntry` gRPC message
  (`catalog.proto`, both the CatalogService and Web copies) gain a `Class` field alongside the
  existing `Category` field, so `ListAllCards` returns it and PictureService's `catalog_client.py`
  can read it without any new RPC.
- A new regression test asserts every category name maps to the same `Class` in every series file
  it appears in (the safety net for the inline-duplication tradeoff above).
- `openspec/GLOSSARY.md` gets a new "Card Class" entry next to the existing "Category" entry,
  defining the relationship between the two.

## Capabilities

### New Capabilities

(none - this extends an existing capability's data, it does not introduce new service behavior)

### Modified Capabilities

- `catalog-service-card-catalog`: `ListAllCards` additionally returns each card's `Class`
  (alongside its existing `Category`), and the catalog's card data enforces that every category
  name maps to exactly one class.

## Impact

- **Data**: all 16 files in `NinjagoScanner.CatalogService/cardInfos/*.json` reshaped.
- **Parsing**: `NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`'s
  `EnumerateCardEntries`/`ShouldTrackCategory` walk is a generic recursive JSON walker that treats
  *any* unrecognized object property name as a nested category-path segment (see its
  `ShouldTrackCategory` exclusion list: `Jahr`, `SortOrder`, `Logo`, `Thema`, `Besonderheiten`,
  `Sondereditionen`, `Kategorien`, `Serie*`). Introducing `Class` and a cards-array wrapper key
  (e.g. `Karten`) inside each category object means **both new property names must be added to
  that exclusion list**, or the walker will treat them as spurious nested category segments
  (e.g. produce a `"Heroes / Karten"` category label instead of `"Heroes"`, and silently discard
  `Class` as an empty nested category with no card entries). This is the crux of the parsing
  change and is detailed further in design.md.
- **Contracts**: `NinjagoScanner.CatalogService/Catalog/CatalogContracts.cs` (`CatalogCardItem`),
  `NinjagoScanner.CatalogService/Protos/catalog.proto` and its duplicate copy
  `NinjagoScanner.Web/Protos/catalog.proto` (kept in sync manually - no shared-file mechanism
  today), `NinjagoScanner.CatalogService/Services/CardCatalogGrpcService.cs` (maps
  `CatalogCardItem` to `CatalogCardEntry`).
- **Consumers**: `picture_service/src/picture_service/catalog_client.py` and
  `picture_service/src/picture_service/models.py` (`SeriesInfo`/future card-level model) gain
  access to `Class` per card once regenerated from the updated proto - consumption is scoped to
  the companion `picture-service-staged-analysis-pipeline` change, not this one.
- **Docs**: `openspec/GLOSSARY.md`.
