## 1. Review page deep link

- [x] 1.1 Make `CollectionQueryService.BuildOwnershipKey` `internal static` and verify `dotnet build NinjagoScanner.slnx` still succeeds
- [x] 1.2 Add `ReviewSession.TryShowCard(seriesName, cardNumber)` (sets review/analysis filters to `All`, clears search, positions `currentIndex` on the matching group via the normalized ownership key; no change and `false` when there is no such group) and cover it in `ReviewSessionTests`: verified-only group is shown, filters cleared, normalized values match, next/previous continue from it, unknown card leaves the default `Unreviewed` filter and index untouched
- [ ] 1.3 In `Review.razor`, add `[SupplyParameterFromQuery]` parameters `series` and `card` and call `TryShowCard` in `OnInitializedAsync` when both are non-blank; verify manually that `/review?series=<name>&card=<nr>` shows that card's group with filters on `All`, and that plain `/review` still starts on `Unreviewed`

## 2. Gallery changes

- [x] 2.1 In `Gallery.razor`, turn the photo tile's media element into an anchor to `/review?series=<escaped>&card=<escaped>` (all categories, puzzle included), keeping the photo-count badge and the non-puzzle flagged badge; verify placeholder tiles remain non-interactive
- [x] 2.2 Remove the Fehlerhaft buttons (tile and lightbox), the lightbox markup/state/handlers, `MarkFehlerhaftAsync` and its helpers, `ReloadSeriesCardsAsync`, and the unused `PictureServiceClient` injection; verify `dotnet build NinjagoScanner.slnx` succeeds with no unused-member warnings from the removal
- [ ] 2.3 Remove the obsolete `.gallery-lightbox-*` and `.gallery-tile-fehlerhaft-btn` rules from `wwwroot/app.css` (and restyle `.gallery-tile-media-button` as needed for an anchor); verify the Gallery grid looks unchanged in the browser for standard and puzzle categories, including at narrow width

## 3. Verify

- [x] 3.1 Run `dotnet test NinjagoScanner.slnx` and verify all tests pass (adjust any test that referenced the removed Gallery members) — Web tests 124/124 pass; `CatalogService.Tests` has 1 pre-existing failure (card 172 name "Wohoo!" vs "Juhu!") that also fails without this change
- [ ] 3.2 With CatalogService, PictureService and Web running, click a standard-tile photo and a puzzle-tile photo in the Gallery and verify the Review page opens on that card's group; set a photo to `Fehlerhaft` there, go back to the Gallery, and verify the standard tile shows the flag badge
