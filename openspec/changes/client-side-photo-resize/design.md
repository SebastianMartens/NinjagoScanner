## Context

See proposal.md - Why. How upload works today (evidence from the code):

- `/upload` (`NinjagoScanner.Web/Components/Pages/Upload.razor`) runs in Interactive Server render mode (`prerender: false`). It has two `InputFile`s: a single-photo input (`capture="environment"`, analysis on upload) and the **Batch Upload** input (`multiple`, optional `webkitdirectory`, `skip_analysis`).
- Single path: `selectedFile.OpenReadStream(maxUploadBytes)` goes to `PictureServiceClient.UploadPhotoAsync(name, size, stream)`. Batch path: each `BatchUploadFile` delegate calls `file.OpenReadStream(Math.Max(maxUploadBytes, file.Size))` and then `UploadPhotoForBatchAsync`. Both end in `StreamUploadAsync`. That method first runs `EnsureUploadIsValid(name, size)` (empty, `MaxUploadBytes` with a 15 MB default, extension in `WebConfig.SupportedExtensions`), then writes the metadata message and 80 KB chunks to the client-streaming `UploadPhoto` RPC.
- With Blazor Server, `OpenReadStream` pulls the bytes from the browser over the SignalR circuit, so a 6 MB phone photo crosses the phone's mobile connection in full before the server sees any of it. Anything that should save that transfer has to run in the browser.
- PictureService (`picture_scanner_service.py` `UploadPhoto`) checks the extension and stores the bytes as they arrive (`photo_store.put_bytes`, no `ContentType`). For AI Analysis it derives the Gemini MIME type **from the file name's extension** (`card_analysis_stage_1_and_2._get_mime_type`). Whatever bytes we upload therefore must match the extension of the source file name.
- PictureService has no image library. Pillow is only used in `scripts/pick_fixtures.py`.
- The rotation flag (`rotated_180`, archive `2026-10-07-rotate-images`) is display-only, by an explicit decision that we never rewrite stored bytes for orientation. This change does not touch that flag. EXIF orientation is a different concern: it is baked in once, at creation time, before the photo is ever stored.

## Goals / Non-Goals

**Goals:**
- Only downscaled bytes cross SignalR, gRPC, S3 and Gemini for JPEG and PNG photos, in both upload paths.
- No new JavaScript, no new package, no PictureService or proto change.
- Failure-safe: in the worst case the system behaves exactly as it does today.

**Non-Goals:**
- Backfilling or re-encoding photos that are already stored.
- Server-side resizing in PictureService (see Decision 4).
- HEIC support (see Decision 3).
- A configurable limit or quality value. 1366 px is a code constant, which can be promoted to config later if ever needed.
- Changing the glossary's stale **Card Photos Directory** (`cardFotos`) entry. That is a separate clean-up.

## Decisions

### 1. Use `IBrowserFile.RequestImageFileAsync`, not custom JS

`RequestImageFileAsync(format, maxWidth, maxHeight)` is built into Blazor's `InputFile`. It runs entirely in the browser: it loads the file into an `Image` via an object URL, computes `scale = min(1, maxW/w, maxH/h)`, draws onto a `<canvas>` of the scaled size and calls `canvas.toBlob(format)`. It returns a new `IBrowserFile` that keeps the original `Name` and has the new `Size`/`ContentType`. Its `OpenReadStream` then transfers only the resized blob. Assessment against the concerns:

| Concern | `RequestImageFileAsync` behaviour | Verdict |
|---|---|---|
| Longest side <= 1366, aspect kept | `maxWidth = maxHeight = 1366` with a uniform scale means the longest side is at most 1366 | OK |
| No upscaling | `min(1, ...)` | OK |
| EXIF orientation | Current browsers (Chrome/Edge 81+, Firefox 77+, Safari/iOS 13.4+) decode `<img>` with `image-orientation: from-image` by default, and `drawImage` uses that oriented image. The canvas output contains no EXIF at all, so the orientation is baked into the pixels and nothing can rotate it twice | OK on current browsers. Verified by manual task 5.3, see Risks |
| Format | Whatever `format` we pass. We pass the input's own type (`image/jpeg` or `image/png`) | OK |
| JPEG quality | No quality parameter, so the browser's default applies (0.92 in Chromium and Firefox). At 1366 px that gives roughly 250-600 KB, legible for card text | Acceptable. We don't need lower |
| Failure signalling | If the image fails to decode, `onerror` never resolves the promise. The .NET call would only end at the circuit's JS interop timeout (1 min by default). If `toBlob` yields `null`, the result has size 0 | Weak. Handled in .NET, see Decision 2 |
| Re-encodes images that are already small | Always draws and re-encodes, even at scale 1 | Handled by the "smaller wins" rule, see Decision 2 |
| Batch | Works on any `IBrowserFile` from `GetMultipleFiles`. Our batch is sequential anyway | OK |

Custom JS (`createImageBitmap(file, { imageOrientation: 'from-image' })` + `OffscreenCanvas.convertToBlob({ quality })`) would give explicit EXIF handling, a quality knob, real rejection on decode errors and the ability to skip images that are already small. But it needs its own plumbing. We would have to find the `File` in `InputFile.Element.files` by index, map it back to `IBrowserFile` order for batches, and return the blob through an `IJSStreamReference`. All of that becomes JS + interop code that bUnit cannot exercise. Every weakness of the built-in API can be closed in a few lines of testable C#, so the custom route doesn't pay for itself. **Decision: use the built-in API.** If manual testing shows wrong orientation on a target browser, switching to the custom-JS variant is the fallback plan. It is contained inside the service from Decision 2, so the page doesn't change.

### 2. One small Web service owns the "prepare for upload" logic

Add `Services/PhotoUploadPreparer.cs` (name is a suggestion) with roughly `Task<IBrowserFile> PrepareAsync(IBrowserFile original, CancellationToken)`:

1. If the extension (via the same `Path.GetExtension` logic as `EnsureUploadIsValid`) is not `.jpg`/`.jpeg`/`.png`, return `original` unchanged. That covers WEBP, BMP and unsupported types. Unsupported types then fail in the existing `EnsureUploadIsValid` with the existing German message, so the type check effectively happens before any resize attempt.
2. Call `original.RequestImageFileAsync(format, 1366, 1366)`, with `format` = `image/png` for `.png` and `image/jpeg` otherwise, wrapped in `.WaitAsync(timeout, cancellationToken)`. The timeout is a constant of about 20 s, well below the 1 min interop timeout and generous for decoding a 50 MP photo on a weak phone.
3. Return `original` if the call throws (except cancellation of the circuit token, which propagates), times out, or yields `Size == 0`, or if `resized.Size >= original.Size`. Otherwise return `resized`.
4. Log every fallback at Warning with its reason (decode error / timeout / empty / not smaller) through the Web's existing OTel logging, without the file name. This makes it visible how often the fallback happens in production.

The "smaller wins" rule (step 3) also covers already-small photos: a 1000x750 JPEG re-encoded at 0.92 is usually not smaller than the original, so the original is kept and no generational loss occurs. In the rare case where a large-pixel but heavily compressed original wins, it is still smaller in bytes than any resize, so the goals (transfer, storage, Gemini payload) are still met. It also keeps the EXIF orientation behaviour exactly as today for such files.

`Upload.razor` calls the service in both paths, before opening the stream, and passes the **prepared** file's `Size` into `UploadPhotoAsync`/`UploadPhotoForBatchAsync`. This is what makes the size limit apply to the uploaded bytes (MODIFIED requirement). The batch delegate's `Math.Max(maxUploadBytes, file.Size)` trick stays, applied to the prepared file. `PictureServiceClient` and `BatchUpload` don't change. Skip-by-name is decided in `BatchUpload` before the delegate runs, so skipped files are never resized.

The single-photo button already shows "Upload läuft..." during the whole call, which covers the resize time too. No new UI text is needed.

Alternative considered: putting the logic inside `PictureServiceClient`. Rejected because that client is a gRPC wrapper that takes a `Stream` and must not depend on Blazor's `IBrowserFile`. A separate class also keeps it unit-testable with a fake `IBrowserFile`.

### 3. Formats: same format out as in. HEIC is not handled

- JPEG goes to JPEG and PNG to PNG, so the extension of the source file name stays truthful. Gemini's extension-based MIME type stays correct, and **Batch Upload**'s skip-by-file-name keeps matching the original names. Re-encoding PNG to JPEG would shrink PNGs more, but it would break one of those two things (a mislabelled MIME type, or renamed files that no longer match on retry), and it would need a PictureService change to sniff MIME from bytes. PNG card photos are rare, and a 1366 px PNG is still far smaller than a full-size one.
- WEBP/BMP: unchanged. `toBlob('image/webp')` silently falls back to PNG on browsers without WebP encoding (Safari), and nothing encodes BMP. Both are rare for phone photos.
- HEIC: with `accept="image/*"` (both inputs), iOS Safari transcodes HEIC to JPEG when the file is handed to the page, so iPhone photos arrive as JPEG and are resized normally. `.heic` files picked on a desktop are already rejected by the extension check, and this change keeps that. A file with a `.jpg` name but HEIC content fails to decode on non-Safari browsers and falls back to the original. That is the same as today.

### 4. No server-side safety net in PictureService

Recommendation: **don't build one.** Reasons:
- The fallback path uploads exactly what is uploaded today, still capped by `MaxUploadBytes` (15 MB). Nothing gets worse than the status quo.
- A safety net would mean adding Pillow to `picture_service`, decoding and re-encoding with `exif_transpose` on a small Fly VM in the upload hot path, plus a second implementation of the same rule to keep in sync. That is a lot of cost for a rare case.
- The Warning log from Decision 2 tells us whether the fallback is ever common. If it is, a server-side resize can be proposed then, with real data.

### 5. Effect on AI Analysis and tests

- AI Analysis: the Gemini payload drops from several MB (base64-inflated by about 33 %) to a few hundred KB per photo, which reduces upload time to Gemini. At 1366 px the card name, card number and series logo stay clearly legible. The seeded `testdata/photos` fixtures are about 60-120 KB already and are what analysis is routinely tested with. No prompt or pipeline change is needed. The manual check (task 5.3) compares analysis results on a real photo.
- `NinjagoScanner.Web.Tests`: new xunit tests for the preparer, using a hand-written fake `IBrowserFile` whose `RequestImageFileAsync` returns a configured file, throws, never completes (for the timeout path), or returns size 0. The timeout is injectable (constructor parameter or `TimeProvider`), so the timeout test runs in milliseconds. `PictureServiceClientUploadValidationTests`, `PictureServiceClientBatchUploadTests`, `BatchUploadTests` and `PictureServiceTestHost` stay as they are because the client API doesn't change. No bUnit test of `Upload.razor` exists today, and bUnit cannot run the browser-side resize, so the page wiring is covered by the manual check.
- `picture_service` pytest: unaffected because PictureService doesn't change.

## Risks / Trade-offs

- [Some browser ignores EXIF orientation in `drawImage` for an image that isn't in the DOM, so phone photos are stored sideways] → Manual check on a real phone photo with an orientation tag on Android Chrome (and iOS Safari if available). If it fails, swap the inside of the preparer for `createImageBitmap(..., { imageOrientation: 'from-image' })` JS (Decision 1 fallback plan). The page and specs don't change.
- [Browser-default JPEG quality is not under our control] → 0.92 is the default in the major engines. That is high, not low, so the risk is slightly bigger files, not illegible ones.
- [Decoding very large photos on low-memory phones fails or is slow] → 20 s timeout plus fallback to the original (today's behaviour).
- [A failed decode costs up to 20 s before the fallback upload starts] → Only in the failure case. For batches, the Warning logs show whether this happens in practice.
- [PNG with transparency] → `toBlob('image/png')` keeps alpha, so no issue (it would only matter for JPEG output, which we don't produce from PNG).
- [Accepting originals above `MaxUploadBytes` once they shrink] → Intended (MODIFIED requirement). The browser does the decode, so the server never holds the large original.

## Migration Plan

Web-only deploy (normal Fly deploy of `NinjagoScanner.Web`). No data migration, no backfill, no proto/PictureService rollout ordering. To roll back, revert the Web change: uploads go back to sending originals, and photos stored in the meantime are ordinary, smaller JPEG/PNG files that every view already handles.
