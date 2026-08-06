## 1. Verify specs against current behavior

- [x] 1.1 Cross-check `picture-service-photo-scan/spec.md` against `Services/PictureScannerGrpcService.cs` (`Scan` method: validation, skip logic, ReviewStatus carry-forward, delay, summary counts)
- [x] 1.2 Cross-check `picture-service-gemini-analysis/spec.md` against `GeminiApiService.cs` (request construction, retry policy, response parsing, status/confidence normalization)
- [x] 1.3 Cross-check `picture-service-series-name-matching/spec.md` against `SeriesCatalogService.ResolveSetName` and its scoring helpers
- [x] 1.4 Cross-check `picture-service-card-listing/spec.md` against `ListCards` in `PictureScannerGrpcService.cs`
- [x] 1.5 Cross-check `picture-service-sidecar-editing/spec.md` against `UpdateSidecar` and `UpdateSetName` in `PictureScannerGrpcService.cs`
- [x] 1.6 Cross-check `picture-service-photo-upload/spec.md` against `UploadPhoto` in `PictureScannerGrpcService.cs`
- [x] 1.7 Cross-check `picture-service-directory-resolution/spec.md` against `ScannerConfig.cs` (`Load`, `ResolveCardPhotosDirectory`, `GetDefaultCardPhotosCandidates`, `TryGetGitMainRepoRoot`)
- [x] 1.8 Fix any spec wording found to be inaccurate against the actual code during 1.1-1.7

## 2. Finalize

- [x] 2.1 Run `openspec validate --change picture-service-baseline --strict` and resolve any issues
- [x] 2.2 Run `/opsx:sync` (or archive the change) to move the seven `picture-service-*` specs into `openspec/specs/`
