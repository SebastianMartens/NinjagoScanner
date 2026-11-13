## Why

Phone camera photos are often several megabytes (4000x3000 px and more). Every byte crosses the SignalR circuit from the phone to the **Web App**, then goes to **Picture Service**, S3 and, base64-encoded, to Gemini for **AI Analysis**. That wastes mobile data, storage and analysis time, although a card is perfectly legible at a much lower resolution. Issue #11 asks for a downscale to at most 1366 px on the longest side, keeping the aspect ratio. Per the owner's decision on #11, the shrinking happens **in the browser, before upload**, so the large original never leaves the phone.

## What Changes

- The `/upload` page shrinks every JPEG or PNG **Card Photo** in the browser before uploading it, so that its longest side is at most 1366 px with the aspect ratio kept. This applies to both the single-photo input and the **Batch Upload** input. Photos that are already small enough are never upscaled.
- The shrunk photo keeps its original format (JPEG stays JPEG, PNG stays PNG) and its original source file name. **Picture Service**, the S3 key scheme and the skip-by-file-name logic of **Batch Upload** therefore do not change.
- Re-encoding bakes the EXIF orientation into the pixels, so phone photos stay upright (this is independent of the display-only 180° rotation flag).
- If shrinking fails or makes the file bigger, the original file is uploaded unchanged, as today. An upload never fails just because shrinking failed.
- The configured maximum upload size (`CardPhotos:MaxUploadBytes`) now applies to the file that is actually uploaded, which is the shrunk one when shrinking succeeds. A 20 MB phone photo that shrinks to 400 KB is accepted.
- WEBP and BMP files are uploaded unchanged (browsers cannot reliably re-encode them in the same format).
- No backfill: photos that are already stored stay as they are.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `web-photo-upload`: adds in-browser downscaling before upload (single and batch), with fallback to the original. The "File type and size validation before upload" requirement changes so that the size limit applies to the bytes actually uploaded.

## Impact

- **NinjagoScanner.Web**: `Components/Pages/Upload.razor` (both upload paths) and a new small service under `Services/` that wraps Blazor's built-in `IBrowserFile.RequestImageFileAsync` with a timeout, a fallback and a "smaller wins" rule. No custom JavaScript, no new package.
- **NinjagoScanner.Web.Tests**: new unit tests for the preparation service, using a fake `IBrowserFile`. `PictureServiceTestHost` and the existing upload/batch tests stay unchanged because the `PictureServiceClient` API is unchanged.
- **picture_service**: no change. No proto change and no new dependency (no Pillow). The pytest suite is unaffected.
- **Infra/CI/deploy**: none.
- Existing photos in S3: untouched. The local `cardFotos` folder is out of scope because the deployed app does not use it.
