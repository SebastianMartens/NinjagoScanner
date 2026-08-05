## 1. Verify specs against current behavior

- [x] 1.1 Cross-check `catalog-service-series-catalog/spec.md` against `Services/CardCatalogGrpcService.cs` and `Catalog/CatalogRepository.cs` (`ListSeries`, `GetSeries`, series merge logic)
- [x] 1.2 Cross-check `catalog-service-card-catalog/spec.md` against `ListAllCards` and the card extraction/sort/dedup logic in `CatalogRepository`
- [x] 1.3 Cross-check `catalog-service-series-metadata/spec.md` against `GetSeriesMetadata` and `ExtractSeriesMetadata`
- [x] 1.4 Cross-check `catalog-service-service-info/spec.md` against `GetServiceInfo`
- [x] 1.5 Cross-check `catalog-service-catalog-refresh/spec.md` against `CatalogRepository.GetSnapshot` and `ComputeCatalogStamp`
- [x] 1.6 Fix any spec wording found to be inaccurate against the actual code during 1.1-1.5

## 2. Finalize

- [x] 2.1 Run `openspec validate --change catalog-service-baseline --strict` and resolve any issues
- [x] 2.2 Run `/opsx:sync` (or archive the change) to move the five `catalog-service-*` specs into `openspec/specs/`
