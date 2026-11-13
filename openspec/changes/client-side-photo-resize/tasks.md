All tasks are Web-only (`NinjagoScanner.Web`, `NinjagoScanner.Web.Tests`). There are no infra/CI/deploy tasks and no PictureService or proto changes.

## 1. Test seam

- [x] 1.1 Add a hand-written `FakeBrowserFile : IBrowserFile` under `NinjagoScanner.Web.Tests/Fixtures/`. Configurable `Name`/`Size`/`ContentType`/content. (Implementation note: `RequestImageFileAsync` is an extension method that throws for any `IBrowserFile` other than Blazor's own type, so it can't be faked on the file. `PhotoUploadPreparer` takes the resize as an internal delegate instead, defaulting to the built-in call. The tests configure that delegate to return a file, throw, never complete or return size 0, and record the `(format, maxWidth, maxHeight)` it was called with.) Verify: `dotnet build NinjagoScanner.slnx` succeeds.

## 2. Preparation service (design Decision 2)

- [x] 2.1 Write failing xunit tests `PhotoUploadPreparerTests` in `NinjagoScanner.Web.Tests/Services/` for:
  - (a) a `.jpg` file calls `RequestImageFileAsync("image/jpeg", 1366, 1366)` and returns the resized file when it is smaller
  - (b) `.jpeg` behaves like (a), with case-insensitive extensions (`IMG.JPG`)
  - (c) `.png` uses `"image/png"`
  - (d) `.webp`, `.bmp` and an unsupported `.gif`/`.heic` return the original and never call `RequestImageFileAsync`
  - (e) the resized file has `Size >= original.Size`, so the original is returned
  - (f) the resized file has size 0, so the original is returned
  - (g) `RequestImageFileAsync` throws `JSException`/`InvalidOperationException`, so the original is returned
  - (h) `RequestImageFileAsync` never completes, so the original is returned after the (injected, short) timeout
  - (i) cancelling the caller's token propagates `OperationCanceledException` and does not fall back

  Verify: `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~PhotoUploadPreparerTests"` compiles and the tests fail.
- [x] 2.2 Implement `Services/PhotoUploadPreparer.cs`. Use a 1366 px constant, about a 20 s default timeout (injectable for tests), `.WaitAsync(timeout, token)` around `RequestImageFileAsync`, the "smaller wins" rule, and a Warning log per fallback with its reason (no file name). Register it in `Program.cs` in DI as scoped or singleton (it holds no state). Verify: all `PhotoUploadPreparerTests` pass.

## 3. Wire into `/upload`

- [x] 3.1 In `Upload.razor` `UploadSelectedFileAsync`, prepare `selectedFile` before opening the stream. Open the **prepared** file's stream and pass its `Size` to `UploadPhotoAsync`. The size limit then applies to the uploaded bytes. Keep the existing `OpenReadStream(maxUploadBytes)` semantics on the prepared file. Verify: `dotnet build NinjagoScanner.slnx` succeeds, and the manual check in 5.3 passes.
- [x] 3.2 In `StartBatchUploadAsync`, call the preparer inside each `BatchUploadFile` delegate (so skipped files are never resized) and open the stream with `Math.Max(maxUploadBytes, prepared.Size)`. Pass `prepared.Size` to `UploadPhotoForBatchAsync`. Verify: `dotnet build` succeeds, `BatchUploadTests` and `PictureServiceClientBatchUploadTests` still pass, and the manual check in 5.4 passes.
- [x] 3.3 Run the full .NET suite. Verify: `dotnet test NinjagoScanner.slnx` is green. In particular `PictureServiceClientUploadValidationTests` passes unchanged, because the client API did not change.
- [x] 3.4 (review follow-up) Introduce `IPhotoUploadPreparer` (registered in DI, injected by `Upload.razor`) and add bUnit `UploadPageTests` with a stubbed preparer, for both the single and the batch path: a large original that the stub shrinks below the limit is uploaded, and an oversized fallback is rejected with nothing streamed. Verify: the tests pass, and fail when the page uses the original's size again.

## 4. Docs

- [x] 4.1 Update the README "Mobile upload (Android)" section. Photos are shrunk in the browser to at most 1366 px (JPEG/PNG) before upload, and `CardPhotos:MaxUploadBytes` applies to the uploaded (shrunk) file. Verify: review the README diff.

## 5. Manual verification with `./dev.ps1 up`

`dev.ps1` binds Web to `http://localhost:5080` only. To reach it from a real Android phone without changing `dev.ps1`, use USB debugging and `adb reverse tcp:5080 tcp:5080`, then open `http://localhost:5080` in Chrome on the phone. Without adb, copy original phone photos (unedited, straight from the camera, with EXIF intact, e.g. via USB/cable rather than a messenger, which strips EXIF) to the PC and upload them from a desktop browser. Single-photo upload needs `GEMINI_API_KEY`. Batch upload does not.

- [ ] 5.1 Start the stack with `./dev.ps1 up`, sign in as `alice`, and open `/upload`. Verify: the page loads and `./dev.ps1 status` shows all services running.
- [ ] 5.2 Prepare test photos: one landscape and one **portrait** photo taken with the phone camera (the portrait one usually carries EXIF orientation 6), plus one photo of 1366 px or smaller and one PNG. Verify: the portrait original shows upright in the phone's gallery.
- [ ] 5.3 Single-photo upload of the portrait phone photo, taken directly with the camera from the phone via adb reverse if available. Verify: in `/gallery` and `/review` the photo shows upright, with the rotation flag not set. Opening the image in a new tab (desktop) shows a longest side of 1366 px (browser tab title or DevTools `naturalWidth`/`naturalHeight`). AI Analysis returns the same series/card number as it does for the original (compare by also uploading the original from the desktop with the change reverted, or against a known card).
- [ ] 5.4 Batch upload of all prepared photos. Verify: every JPEG/PNG is stored with a longest side of at most 1366 px, the photo of 1366 px or smaller is not enlarged, the PNG is still a PNG, all names appear in the collection, and re-selecting the same files skips all of them as "bereits vorhanden". Also run one large-folder batch (several hundred full-size phone photos) while watching the tab's memory (Chrome Task Manager, Shift+Esc, or DevTools Memory). Memory is expected to grow with the batch because Blazor keeps every resize result until the input is replaced (see design Risks). Note the growth per 100 photos, and whether the tab survives.
- [ ] 5.5 Fallback check. Upload a file with a `.jpg` name and non-image content (e.g. a renamed `.txt`) through the batch input. Verify: it doesn't hang longer than the timeout, the fallback Warning appears in `./dev.ps1 logs web` (or the Aspire Dashboard), and the file then fails or succeeds exactly as it would without this change (PictureService stores it, and no resize error is shown).
- [ ] 5.6 Bandwidth sanity check (desktop DevTools, Network tab, WS frames on `_blazor`). Verify: uploading a roughly 5 MB photo transfers only a few hundred KB over the circuit.
