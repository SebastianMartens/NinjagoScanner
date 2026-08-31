## 1. Review page display cap

- [x] 1.1 In `NinjagoScanner.Web/Components/Pages/Review.razor`, change the photo grid loop to iterate `group.Photos.Take(18)` instead of `group.Photos`, keeping every existing per-tile behavior (status control, series picker, card number/language edits, details toggle, delete) unchanged for the displayed tiles.
- [x] 1.2 Add a "more photos exist" message shown only when `group.Photos.Count > 18`, positioned near the group header/summary, stating that more photos exist beyond the 18 shown.
- [x] 1.3 Verify `ConfirmAllAsync` iterates only the same capped set the page renders (it already iterates `group.Photos` from `CurrentGroup` — update it to iterate the same `Take(18)` sequence used for rendering, e.g. via a shared computed property, so "Confirm all" cannot act on photos the user can't see).

## 2. Verification

- [x] 2.1 Add/update a test in `NinjagoScanner.Web.Tests` covering: a group with <=18 photos shows all of them and no message; a group with >18 photos shows only the first 18 (in existing sort order) plus the message.
- [x] 2.2 Add/update a test confirming "Confirm all" on a group with >18 photos only sets `ReviewStatus` to `verified` for the displayed 18, leaving the rest unchanged.
- [x] 2.3 Run `dotnet test NinjagoScanner.Web.Tests` and confirm it passes.
- [x] 2.4 Manually load `/review` against a dataset with a large catch-all group (or simulate one) and confirm the page loads noticeably faster and the message appears. (Partially verified: all 3 services started against the configured prod S3/DynamoDB data and `/review` returned HTTP 200 with no server-side exception. Full interactive verification — actually seeing the capped grid and message rendered — was not done: this environment has no browser automation tooling, and the page prerenders disabled, so a plain HTTP fetch doesn't show the Blazor Server-rendered content after the SignalR circuit connects. The capping/message logic itself is covered by the unit tests in 2.1/2.2, which pass.)
