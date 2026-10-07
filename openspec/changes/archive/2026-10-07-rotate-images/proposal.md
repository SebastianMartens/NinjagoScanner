## Why

Photos uploaded via the mobile upload flow sometimes land upside down (the phone camera's orientation isn't always captured correctly). There's currently no way to fix this in the app — the only workaround is deleting and re-uploading the photo. A simple, non-destructive rotate control on the Review page lets a person fix this in place.

## What Changes

- Add a `Rotated180` flag to the sidecar record (part of the Judged section, alongside `ReviewStatus`): a human-only, display-only piece of metadata — not something AI Analysis ever sets.
- Add a new `UpdateRotation` RPC to PictureService, following the same shape as the existing single-field sidecar edits (`UpdateSetName`, `UpdateCardNumber`, etc.): scoped to a collection, creates a sidecar record (reporting `notAnalyzed`) if none exists yet, otherwise flips only the rotation flag.
- Add a rotate control to the Review page that toggles a photo's `Rotated180` flag.
- Wherever a photo is rendered in the Web app (review, gallery, collection detail — the Overview page shows only per-card counts, no photo images), apply the stored rotation as a display-only transform — the underlying photo bytes in S3 are never modified. 180° rotation doesn't change the image's displayed width/height, so no layout/aspect-ratio handling is needed.
- No re-analysis is triggered by a rotation change; `AnalysisStatus` is unaffected.

## Capabilities

### New Capabilities
- `web-photo-rotation`: the Review page's rotate control, and photos rendering with their stored rotation applied consistently across every Web view that displays a photo (review, gallery, collection detail).

### Modified Capabilities
- `picture-service-sidecar-editing`: adds a sixth single-field edit RPC, `UpdateRotation`, alongside the existing five.
- `picture-service-sidecar-sections`: the Judged section's fixed field set grows to include the rotation flag.

## Impact

- **PictureService** (`picture_service/`): `models.py` (`SidecarRecord.rotated_180`), `sidecar_table.py`/`sidecar_store.py` (persist/read the new field), `picture_scanner_service.py` (new `UpdateRotation` RPC handler).
- **Shared gRPC contract**: `NinjagoScanner.Web/Protos/picture_service.proto` (canonical copy) gains the `UpdateRotation` RPC and a `rotated_180` field on the photo/card message(s); PictureService's proto codegen reads this file directly, so both sides regenerate stubs.
- **NinjagoScanner.Web**: `Services/PictureServiceClient.cs` (new client method), `Services/CollectionQueryService.cs` (surface the flag on the view models it builds for review/gallery/collection detail), `Components/Pages/Review.razor` (rotate button + CSS transform), and the gallery/collection-detail render paths (apply the same CSS transform). The Overview page shows no photo images, so it needs no change.
- **Tests**: `NinjagoScanner.Web.Tests` (`PictureServiceTestHost.cs` fake needs the new RPC), `picture_service/tests` (`uv run pytest`).
- No change to S3 photo bytes, no new dependency in PictureService, no change to AI Analysis.
