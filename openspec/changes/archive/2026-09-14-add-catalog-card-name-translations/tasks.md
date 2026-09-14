## 1. Parsing and resolution

- [x] 1.1 Update `CatalogRepository.EnumerateCardEntries`/`ExtractSeriesCards` (`NinjagoScanner.CatalogService/Catalog/CatalogRepository.cs`) to read `Name` as an object of language code → name string (e.g. `{"de": "Kai", "en": "Kai"}`) instead of a plain string, and resolve it to a single value preferring `"de"`, falling back to `"en"`; verify by running the existing `CatalogRepositoryTests` suite after updating fixtures in task 2 — all still pass with resolved names matching the pre-migration values.
- [x] 1.2 Update the "incomplete entry" exclusion check so an entry with a card number but no name in either `"de"` or `"en"` is excluded, matching the updated `catalog-service-card-catalog` spec; verify with a new test asserting such an entry is absent from `GetSnapshot().Cards`.

## 2. Test fixture updates

- [x] 2.1 Update inline JSON fixtures in `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/ParsingTests.cs` from `"Name": "Kai"` to the new per-language shape (e.g. `"Name": {"de": "Kai"}`), keeping each test's asserted resolved `CardName` unchanged; verify `dotnet test NinjagoScanner.CatalogService.Tests --filter "FullyQualifiedName~ParsingTests"` passes.
- [x] 2.2 Update fixtures in `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/DedupMergeCachingTests.cs` and `NinjagoScanner.CatalogService.Tests/CatalogRepositoryTests/NormalizationAndSortingTests.cs` the same way; verify both test classes pass.
- [x] 2.3 Update fixtures in `NinjagoScanner.CatalogService.Tests/Services/CardCatalogGrpcServiceTests.cs`; verify `dotnet test NinjagoScanner.CatalogService.Tests` passes in full.
- [x] 2.5 (found during implementation, not in the original plan) Update the same `"Name": "X"` inline JSON fixtures in `NinjagoScanner.Web.Tests` (`CollectionQueryServiceAnalysisStatusCountsTests.cs`, `CollectionQueryServiceCardNumberBeforeCategoryTests.cs`, `CollectionQueryServiceGalleryTests.cs`, `CollectionQueryServiceReviewGroupsTests.cs`, `PictureServiceClientDeletePhotoTests.cs`) — these also spin up an in-process `CatalogServiceTestHost` from inline JSON and were missed when scoping task 2; verify `dotnet test NinjagoScanner.Web.Tests` passes in full.
- [x] 2.4 Add a test covering English-only resolution (name map has `"en"` but no `"de"` → resolved `CardName` is the English value), and a test covering the new dedup requirement (two entries for the same series/card number with name maps populated in different single languages collapse into one result); verify both pass.

## 3. Data migration

- [x] 3.1 Migrate `NinjagoScanner.CatalogService/cardInfos/series_3.json` and `series_8NL.json`: wrap every card's existing `Name` string as `{"de": "<existing value>"}`, changing no other content in the files; verify with a diff review showing only `"Name": "X"` → `"Name": {"de": "X"}` structural changes, no value text changed.
- [x] 3.2 Migrate the remaining 14 series files (`series_1`, `2`, `4`, `5`, `5NL`, `6`, `6NL`, `7`, `7NL`, `8`, `9`, `9NL`, `10`, `11`) the same way, wrapping existing values as `{"en": "<existing value>"}`; verify with the same diff review.
- [x] 3.3 Run `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` to confirm the full solution (including PictureService/Web tests that spin up an in-process CatalogService) still passes unchanged, since `CardCatalogItem.CardName` values must be byte-identical to before the migration.

## 4. Verification

- [x] 4.1 Start `NinjagoScanner.CatalogService` (`dotnet run` in that project) and call `ListAllCards`/`ListSeries` (e.g. via the Web app's Overview/collection pages, or grpcurl) to manually confirm every card still shows its previous (German-or-English, whichever it had) display name with no blanks or `null`s introduced by the migration.
