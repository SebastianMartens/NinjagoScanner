## Why

AI Analysis can only be triggered today by uploading a new photo (`UploadPhoto`) or by the bulk
`Scan` backfill, which skips every photo that already has a completed (`ok`/`uncertain`) analysis.
A person working through the Review page who sees a wrong or failed detection - a misread card
number, a `failed` status from a transient Gemini error, or a photo analyzed before the staged
pipeline shipped - has no way to ask for a fresh analysis of just that photo. Their only options
are fixing every field by hand or deleting and re-uploading the photo. A per-photo re-analysis
button closes that gap right where the problem is noticed.

## What Changes

- New PictureService RPC `ReanalyzePhoto(photo_id, collection_id)`: re-runs the full staged AI
  Analysis pipeline on one already-stored photo and writes the result to its sidecar, replacing
  the previous analysis result. Returns the updated `CardEntry`.
  - The photo's `ReviewStatus` is preserved, and a `verified` photo's series and card number stay
    pinned - the same rules `Scan` already applies when it re-analyzes an existing sidecar.
  - A transport-level failure (Gemini unreachable) is reported to the caller and leaves the
    existing sidecar untouched, so a transient outage cannot overwrite a good analysis with a
    `failed` one. A content-level failure is recorded as `failed`, as on every other entry point.
- Each photo tile on the Review page gets a "Neu analysieren" button that calls the RPC, shows
  progress on that tile while the analysis runs (it makes two Gemini calls, so it takes seconds),
  then refreshes the page data - including the photo's expanded details if open - and shows an
  inline error on the tile if the re-analysis could not be performed.
- `NinjagoScanner.Web`'s `PictureServiceClient` gains `ReanalyzePhotoAsync`; the Web test host's
  fake `CardPictureService` gains a matching override.
- `picture_service.proto` gains the RPC and its request/response messages (additive, not breaking).

Out of scope: a re-analyze button on the Collection page, bulk/group-level re-analysis ("re-analyze
all in this group"), and any change to the analysis pipeline itself.

## Capabilities

### New Capabilities
- `picture-service-photo-reanalysis`: the `ReanalyzePhoto` RPC - request validation, what is
  re-run, which sidecar fields are preserved versus overwritten, and failure semantics.

### Modified Capabilities
- `web-card-review-flow`: adds a requirement that each Review-page photo tile has a re-analysis
  control, with its busy/refresh/error behavior.

## Impact

- **Code**: `NinjagoScanner.Web/Protos/picture_service.proto` (canonical copy);
  `picture_service/src/picture_service/picture_scanner_service.py` (new RPC; the
  analyze-and-store steps shared with `UploadPhoto` are factored into a helper);
  `NinjagoScanner.Web/Services/PictureServiceClient.cs`;
  `NinjagoScanner.Web/Components/Pages/Review.razor` (+ `wwwroot/app.css` if the button needs
  styling); `NinjagoScanner.Web.Tests/Fixtures/PictureServiceTestHost.cs`.
- **Cost**: each click makes the same two Gemini calls an upload does. No rate limiting is added;
  the button is disabled while its photo's re-analysis is in flight.
- **Dependency**: builds on `picture-service-staged-analysis-pipeline` (`analyze_card`'s
  `verified` parameter and the `Detected`/`Derived`/`Judged` sidecar shape). Implement after that
  change is merged, or on top of its working tree.
- **Not affected**: photo storage, `Scan`'s skip/batch behavior, `UploadPhoto`, the
  `AnalysisStatus`/`ReviewStatus` semantics, and CatalogService.
