## Why

The Gallery's per-tile "Fehlerhaft" button (and the matching button in the puzzle lightbox) existed to flag misread photos directly from the grid. Since the AI analysis was improved, flagging is rarely needed there, and the button clutters every tile. Meanwhile, spotting a wrong-looking photo in the Gallery leaves the user with no way forward: the review page is the place to inspect, correct, and flag photos, but reaching the right card there means manually searching for it.

## What Changes

- **BREAKING (UI)**: Remove the "Fehlerhaft" control from Gallery card tiles and the Gallery lightbox.
- Clicking or tapping a Gallery card tile that shows a photo now navigates to the Review page, positioned on the group for that card (same series and card number) — all photos of that group are shown, as usual on the Review page. This replaces the in-place lightbox, which is removed entirely (puzzle tiles included).
- The Review page accepts a deep link (series + card number) that selects that card's group. So the group is actually visible, a deep link ignores the page's default `Unreviewed` filter and starts with all filters cleared (`All` / `All` / empty search).
- Users set the `Incorrect` ("Fehlerhaft") review status on the Review page via its existing three-segment status control; no change there.
- The read-only flagged indicator on non-puzzle Gallery tiles stays, so a photo flagged on the Review page is still visibly flagged in the grid.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `web-gallery-page`: remove the Fehlerhaft-control requirement and the lightbox behavior; a photo tile click navigates to the Review page for that card instead; the flagged indicator becomes grid-only (no lightbox indicator for puzzle tiles).
- `web-card-review-flow`: add a requirement that the Review page can be opened on a specific card's group via series + card number, and adjust the `Unreviewed` filter default to not apply when opened this way.

## Impact

- `NinjagoScanner.Web/Components/Pages/Gallery.razor`: tile markup (link to Review instead of lightbox button; no Fehlerhaft button), removal of lightbox and mark-as-fehlerhaft code, `PictureServiceClient` no longer injected.
- `NinjagoScanner.Web/Components/Pages/Review.razor` and `NinjagoScanner.Web/Services/ReviewSession.cs`: read `series`/`card` query parameters and position the session on the matching group with cleared filters.
- `NinjagoScanner.Web/wwwroot/app.css`: remove the Fehlerhaft-button and lightbox styles.
- Tests in `NinjagoScanner.Web.Tests` (`ReviewSessionTests` for the new positioning).
- No gRPC/proto, CatalogService, or PictureService changes.
