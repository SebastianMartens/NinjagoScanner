## 1. Parser safety net (do this before touching any data file)

- [ ] 1.1 Add a test that captures `ExtractSeriesCards`'s output (the full set of `(SeriesName, Category, CardNumber, CardName, SortOrder)` tuples) for every current `cardInfos/*.json` file, and verify it passes against today's unmodified data
- [ ] 1.2 Add `Class` and `Karten` to `ShouldTrackCategory`'s exclusion list in `CatalogRepository.cs`, and verify the test from 1.1 still passes unmodified (proves the parser change alone is a no-op against today's flat-array shape)

## 2. Reshape cardInfos/*.json

- [ ] 2.1 For each of the 16 `NinjagoScanner.CatalogService/cardInfos/*.json` files, wrap every category's card array as `{ "Class": "<value from design.md's mapping table>", "Karten": [ ...unchanged... ] }`, and verify the 1.1 snapshot test still passes after each file (category labels, card counts, and card content must be identical to before the reshape)
- [ ] 2.2 Verify every category in every file has a non-empty `Class` value from the fixed set (`character`, `action`, `vehicle`, `puzzle-piece`, `trap`, `limited edition`, `art`)

## 3. Catalog loading fails fast on a missing Class

- [ ] 3.1 Update `CatalogRepository.cs` to throw a clear, file-identifying error when a category object has no `Class` property, and add a test with a deliberately-missing `Class` fixture confirming catalog loading fails rather than defaulting

## 4. Class-consistency regression test

- [ ] 4.1 Add a test asserting every category name maps to the same `Class` across all 16 series files (loads the real `cardInfos/*.json` files, groups by category name, asserts one distinct class per group), and verify it passes against the reshaped data from step 2

## 5. Contracts

- [ ] 5.1 Add `Class` to `CatalogCardItem` in `CatalogContracts.cs` and populate it in `CatalogRepository.cs`'s card-building step
- [ ] 5.2 Add a `class` field to the `CatalogCardEntry` message in `NinjagoScanner.CatalogService/Protos/catalog.proto`, and mirror the identical change in `NinjagoScanner.Web/Protos/catalog.proto` (kept in sync manually today - verify a `diff` of the two files is empty after editing)
- [ ] 5.3 Populate `Class` in `CardCatalogGrpcService.cs`'s `CatalogCardItem` → `CatalogCardEntry` mapping, and verify `dotnet build NinjagoScanner.slnx` succeeds (regenerates both projects' gRPC stubs)
- [ ] 5.4 Add/extend a `CardCatalogGrpcService` test asserting `ListAllCards` returns the expected `class` for a known card, and verify it passes

## 6. Docs

- [ ] 6.1 Add a "Card Class" entry to `openspec/GLOSSARY.md` next to the existing "Category" entry, describing the fixed 7-value set and its relationship to Category (each Category maps to exactly one Class)

## 7. Full verification

- [ ] 7.1 Run `dotnet test NinjagoScanner.slnx` and verify all tests pass, including the new snapshot, fail-fast, consistency, and gRPC tests added above
