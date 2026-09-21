## 1. Ordering

- [x] 1.1 In `CollectionQueryService.BuildReviewGroups`, insert the catch-all group at index 0 instead of appending it, and update its doc comment ("appends the catch-all group" → "puts the catch-all group first"); verify by `dotnet build NinjagoScanner.slnx` succeeding
- [x] 1.2 Update the "trailing bucket" wording in the `CardReviewGroup` doc comment to "leading bucket"; verify the comment no longer says trailing/last

## 2. Tests

- [x] 2.1 In `CollectionQueryServiceBuildReviewGroupsTests`, rename `BuildReviewGroups_OrdersBySeriesThenCardNumber_AndPutsCatchAllLast` to `..._AndPutsCatchAllFirst` and assert `groups[0].IsCatchAll` with the matched groups following in series/card order; verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~BuildReviewGroups"` passes
- [x] 2.2 In `CollectionQueryServiceReviewGroupsTests`, adjust the group indices (catch-all is now `groups[0]`, matched groups shift by one); verify `dotnet test NinjagoScanner.Web.Tests --filter "FullyQualifiedName~ReviewGroups"` passes
- [x] 2.3 Add a test that with no unresolved photos `BuildReviewGroups` returns no catch-all group and the first group is the lowest-ordered matched card; verify it passes
- [x] 2.4 Add a `ReviewSession` test that a new session with an unresolved photo starts (`CurrentGroup`) on the catch-all group and `GoToNext` moves to the first matched group; verify it passes

## 3. Verification

- [x] 3.1 Run `dotnet test NinjagoScanner.slnx` and verify all tests pass (fix any other test that assumed the catch-all group is last)
