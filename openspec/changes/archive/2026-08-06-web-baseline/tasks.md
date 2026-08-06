## 1. Verify specs against current behavior

- [x] 1.1 Cross-check `web-card-gallery/spec.md` against `Components/Pages/Home.razor`
- [x] 1.2 Cross-check `web-card-table-view/spec.md` against `Components/Pages/CardsTable.razor`
- [x] 1.3 Cross-check `web-photo-upload/spec.md` against `Components/Pages/Upload.razor` and `Services/CardCatalogService.SaveUploadedPhotoAsync`
- [x] 1.4 Cross-check `web-collection-overview/spec.md` against `Components/Pages/Collection.razor` and the `CardCatalogService` collection/sidecar methods
- [x] 1.5 Cross-check `web-app-configuration/spec.md` against `Program.cs`
- [x] 1.6 Fix any spec wording found to be inaccurate against the actual code during 1.1-1.5

## 2. Finalize

- [x] 2.1 Run `openspec validate --change web-baseline --strict` and resolve any issues
- [x] 2.2 Archive the change to move the five `web-*` specs into `openspec/specs/`
