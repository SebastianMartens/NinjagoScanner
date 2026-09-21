## 1. Pure grouping step (D1)

- [ ] 1.1 In `CollectionQueryService`, add a snapshot type (catalog cards + photo list) and a fetch method that returns it; verify it compiles and returns the same data `GetReviewGroupsAsync` used internally.
- [ ] 1.2 Move the grouping/sorting/catch-all logic out of `GetReviewGroupsAsync` into a static, network-free `BuildReviewGroups(catalog, photos)` that reuses the given `CardListItem` instances, and make `GetReviewGroupsAsync` = fetch + build; verify `CollectionQueryServiceReviewGroupsTests` passes unchanged.
- [ ] 1.3 Add unit tests calling `BuildReviewGroups` directly (no service host): ordering by series then card number, catch-all group last, same photo instances returned (reference equality), and a photo whose series/card number changes lands in the matching group; verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~ReviewGroups"` passes.

## 2. Review session state (D2, D7)

- [ ] 2.1 Add the `ReviewSession` state class (flat photo list, catalog, groups, active filters, current index, cached filtered-group list) with `ReplacePhoto`, `ReplacePhotos`, and `RemovePhoto`, regrouping via `BuildReviewGroups` after each mutation; verify with unit tests that a mutation leaves other photo instances untouched (same `ImageUrl`).
- [ ] 2.2 Move the position rules into the session: stay on the group with the same key if it still exists, otherwise the group now at the previous index clamped to the end; move `FindNextFilteredIndexAfter` and the filter predicates (`MatchesFilters` and helpers) with them and keep the existing `Review*Tests` passing; verify with tests for: status change keeps position, series change moves a photo out of the current group, deleting the last photo in a group advances, deleting the last photo overall yields the empty state.
- [ ] 2.3 Recompute the filtered-group list only when groups or a filter value change; verify with a test that repeated reads return the same list instance until a mutation or filter change.

## 3. Review page wiring (D3, D5, D6)

- [ ] 3.1 Change `Review.razor` to build a `ReviewSession` in `OnInitializedAsync` from one fetch (known series, catalog, photos) and read groups/current group/filtered list from it; verify the page loads and shows the same first group as before (manual check with the Web app running).
- [ ] 3.2 Replace `ReloadGroupsKeepingPositionAsync` in `RunPhotoActionAsync` with a local patch applied after the RPC succeeds: review status, language, series (`SetName`) and card number use the same normalization as `PictureServiceClient` before patching; delete removes the photo; on RPC failure nothing is patched. Verify with tests on the session/normalization helpers (blank and whitespace card number/series) and, manually, that a status click no longer triggers `ListCards` or `ListAllCards` (check gRPC/OTel traces or service logs).
- [ ] 3.3 Update `ReanalyzeAsync` to patch the photo from the returned card while keeping its existing `ImageUrl`, still refreshing the cached details as today; verify with a session-level test that a re-analysis which changes the card number moves the photo to the matching group and keeps the `ImageUrl`.
- [ ] 3.4 Re-sync card-number drafts from the session after each mutation so tiles show current values, without discarding an unsaved draft on a photo the action did not touch; verify manually by typing a draft on one tile, changing another tile's status, and confirming the draft is still there and saved values display correctly.
- [ ] 3.5 Make "Von vorne beginnen" re-fetch the snapshot, build a fresh session, reset the index to 0, and re-sync drafts; verify manually that data changed outside the page (e.g. an upload in another tab) shows up after activating it.

## 4. Confirm all (D4)

- [ ] 4.1 Change `ConfirmAllAsync` to save only displayed photos whose `ReviewStatus` is not `verified` (case-insensitive), patching each photo as its RPC succeeds and advancing with the existing confirm-all rule once all succeed; make no RPC and just advance when none qualify. Verify with tests using a recording fake of the update call: mixed statuses save only `unreviewed`/`incorrect` photos, an all-verified group makes zero calls, and the group advances.
- [ ] 4.2 Verify partial failure with a fake whose Nth call throws: photos before N show `verified`, photo N and later keep their previous status, the page stays on the group, and the exception surfaces.
- [ ] 4.3 Verify Confirm all under each filter setting: with the `Unreviewed` filter the confirmed group leaves the list and the next matching group is shown; when it was the last one the empty state is shown; groups over the 18-photo cap leave hidden photos untouched.

## 5. Cleanup and end-to-end check

- [ ] 5.1 Remove now-unused reload code and fields from `Review.razor` (`ReloadGroupsKeepingPositionAsync`, the direct `groups` field and property) and update any existing review tests that assumed a reload; verify `dotnet build NinjagoScanner.slnx` and `dotnet test NinjagoScanner.slnx` both pass.
- [ ] 5.2 With CatalogService, picture_service and Web running against real data, click through status, language, series, card number, delete, re-analyze and "Alle bestätigen" on `/review`; verify each completes without a collection re-fetch, images in the visible group are not re-requested after an action (browser network panel), and the resulting page matches what a browser refresh shows.
