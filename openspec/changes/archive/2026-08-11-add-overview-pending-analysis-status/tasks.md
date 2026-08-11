## 1. Model

- [x] 1.1 Add a `NotAnalyzed` count property to `PhotoAnalysisStatusCounts` in `NinjagoScanner.Web/Models/SeriesSummaryItem.cs`.

## 2. Service

- [x] 2.1 Update `CardCatalogService.BuildAnalysisStatusCounts` in `NinjagoScanner.Web/Services/CardCatalogService.cs` to count every photo entry whose `AnalysisStatus` is not `ok`/`uncertain`/`failed` (case-insensitive) into `NotAnalyzed`, so the four counts always sum to `entries.Count`.
- [x] 2.2 Add/update a test (alongside `CardCatalogServiceGalleryTests.cs` or similar) covering: photos with `unknown` status are bucketed as not-yet-analyzed, and the four counts sum to the total photo count.

## 3. UI

- [x] 3.1 Update `Overview.razor` to display the `NotAnalyzed` count in the analysis-status statistics row (German label, consistent with the existing "ok"/"unsicher"/"fehlgeschlagen" labels, e.g. "noch nicht analysiert").

## 4. Verification

- [x] 4.1 Run `dotnet test` for `NinjagoScanner.Web.Tests` and confirm it passes.
- [x] 4.2 Manually load the Overview page and confirm the four analysis-status counts sum to the total photo count.
