## 1. Web: Collection/Membership schema

- [ ] 1.1 Add `Collection` (`Id`, `Name`, `CreatedAt`) and `CollectionMembership` (`CollectionId`, `UserId`, `Role`: `Owner`|`Reader`, composite key) entities to `AppDbContext`, and verify the model builds and the new DbSets are queryable in a local run.
- [ ] 1.2 Add an EF Core migration for these two tables (introducing real migrations alongside the existing `EnsureCreated()` call — see `design.md` Migration Plan step 1) and verify `dotnet ef database update` (or the app's startup migration step) creates both tables in `users.db` without touching `AspNetUsers`.

## 2. Web: Registration creates a Collection

- [ ] 2.1 Update the registration flow (`Register.cshtml.cs`) to create a `Collection` and an `Owner` `CollectionMembership` for the new user in the same transaction as `AppUser` creation, and verify via a test per the `web-collections` "Collection created at registration" scenarios (success creates both; failure creates neither).

## 3. Web: Current-collection resolution and authorization

- [ ] 3.1 Add a scoped `ICurrentCollectionContext` (or equivalent) that resolves the acting user's current collection via a live `CollectionMemberships` query against `HttpContext.User`, defaulting to the collection where the user holds `Owner`, and verify it returns the correct collection for a logged-in test user.
- [ ] 3.2 Add an authorization check (policy or explicit guard) requiring `Owner` role on the resolved collection for every page/action, layered on top of the existing `RequireAuthenticatedUser()` fallback policy, and verify per the `web-collections` "Every page and action requires the Owner role" scenarios (owner allowed; no-Owner-membership denied).
- [ ] 3.3 Register `IHttpContextAccessor` in DI (not present today) as needed to support the above.

## 4. Web: gRPC client plumbing

- [ ] 4.1 Change `PictureServiceClient` and `CollectionQueryService` registration from `AddSingleton` to scoped, while keeping the underlying `GrpcChannel` as a singleton dependency injected into them, and verify `web-grpc-client-connection-reuse`'s existing tests still pass (one channel per service, reused across calls).
- [ ] 4.2 Have `PictureServiceClient` resolve `collection_id` once per circuit from `ICurrentCollectionContext` and attach it to every outgoing collection-scoped request internally, without adding a new parameter to its existing public C# method signatures, and verify existing Web call sites compile unchanged.

## 5. Web: Navigation collection indicator

- [ ] 5.1 Display the current collection's name in `NavMenu.razor`, and verify it renders correctly for a logged-in test user per the `web-collections` "Navigation displays the current collection" scenario.

## 6. PictureService: proto contract

- [ ] 6.1 Add `string collection_id` to `ScanRequest`, `UploadPhotoMetadata`, `ListCardsRequest`, `GetPhotoDownloadUrlRequest`, `GetCardDetailsRequest`, `UpdateSidecarRequest`, `UpdateSetNameRequest`, `UpdateCardNumberRequest`, `UpdateCardLanguageRequest`, `UpdateReviewStatusRequest`, and `DeletePhotoRequest` in `Protos/picture_service.proto` (leaving `MigrateSidecarsRequest` unchanged per `design.md`), and verify the project builds (proto codegen succeeds) and `NinjagoScanner.Web`'s references to the regenerated client types still compile once call sites are updated.

## 7. PictureService: storage layer rekeying

- [ ] 7.1 Change `PhotoStore.cs`'s S3 key scheme from `photos/<photo_id>` to `photos/<collection_id>/<photo_id>` (`BuildObjectKey`/`KeyPrefix`), and verify unit tests cover key construction for a given collection/photo pair.
- [ ] 7.2 Create the new DynamoDB sidecar table with partition key `CollectionId` and sort key `PhotoId` (`SidecarTable.cs`), and verify a write/read round-trip against the new table in a test.
- [ ] 7.3 Change `SidecarCache.cs`'s cache key from `photoId` to `(collectionId, photoId)`, and verify per the `picture-service-sidecar-cache` "Cache entries are keyed by collection and photo together" scenario.
- [ ] 7.4 Update `ListCards`/`Scan` (and any other bulk read) to query DynamoDB/S3 scoped to one `collection_id` (a `Query`, not a full `Scan`), and verify per the `picture-service-card-listing`/`picture-service-photo-scan` "excludes other collections" scenarios.

## 8. PictureService: RPC enforcement

- [ ] 8.1 Reject any collection-scoped RPC call with a missing/empty `collection_id` with `InvalidArgument`, and verify per `collection-scoped-picture-access`'s "Missing collection_id" scenario.
- [ ] 8.2 Validate that a given `collection_id` corresponds to a collection PictureService has recorded (created via upload/migration), returning not-found otherwise, without checking caller identity/role, and verify per `collection-scoped-picture-access`'s "Unknown collection_id"/"performs no membership check" scenarios.
- [ ] 8.3 Update `GetPhotoDownloadUrl` and `DeletePhoto` to resolve/act only within the given `collection_id`, returning not-found for a photo ID that exists only in a different collection, and verify per `picture-service-photo-download` and `picture-service-photo-deletion`'s new scenarios.
- [ ] 8.4 Update `UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, and `UpdateReviewStatus` to create/update sidecars only within the given `collection_id`, and verify per `picture-service-sidecar-editing`'s "same file name in two collections is edited independently" scenario.
- [ ] 8.5 Update `UploadPhoto` to store the new photo within the given `collection_id`, and verify per `picture-service-photo-upload`'s "Upload is stored within the requesting collection" scenario.

## 9. Data migration

- [ ] 9.1 Write a one-time script/tool that creates a `Collection` + `Owner` `CollectionMembership` for the existing user "anton" in Web's SQLite database, and verify the row exists after running it.
- [ ] 9.2 Write a one-time script/tool that copies every existing DynamoDB sidecar item into the new table under anton's `collection_id`, and every existing S3 object from `photos/<photo_id>` to `photos/<collection_id>/<photo_id>` under anton's `collection_id`, without deleting the originals, and verify by comparing item/object counts before and after and spot-checking a few photo IDs.
- [ ] 9.3 Verify anton can log in and see all pre-existing cards in `ListCards`/the collection list page after cutover, and that no other account can.

## 10. Coordinated deployment

- [ ] 10.1 Deploy PictureService and Web together (per `design.md` Migration Plan step 4), run the data migration during the maintenance window, then verify the app end-to-end: registration creates a collection, upload/scan/review/delete all operate within the acting user's collection, and anton's pre-existing data is intact and isolated from other accounts.
