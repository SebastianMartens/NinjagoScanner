## Why

ASP.NET Core Identity login gates the Web app, but it's purely an access gate: every logged-in user sees exactly the same shared PictureService data (all photos, all sidecars). Multiple people using the app today can see and edit each other's scanned cards, with no way to keep collections private or to later share one collection deliberately.

## What Changes

- Introduce **Collection** as a first-class concept: a Collection holds a set of Card Photos + Sidecars. Every user account is automatically given its own Collection at registration time and is recorded as that Collection's **Owner**.
- Introduce a **Collection Membership** model (`Collection` × `AppUser` × `Role`) with `Owner` and `Reader` roles, stored in Web's existing SQLite database. Only `Owner` is enforced in this change; `Reader` (read-only sharing) is modeled now so it can be turned on later without a schema change. Creating additional collections, renaming, and sharing UI are explicitly out of scope for this change.
- **BREAKING**: Every PictureService RPC that reads or writes photo/sidecar data now requires an explicit `collection_id`. Callers that omit it are rejected.
- Rekey PictureService's storage by collection: S3 objects move from `photos/<photo_id>` to `photos/<collection_id>/<photo_id>`, and the DynamoDB sidecar table's key schema changes from a `PhotoId`-only partition key to a `CollectionId` partition key + `PhotoId` sort key (requires a new table, not an in-place alter).
- Web resolves "the current user's collection" per request from a live SQLite membership lookup (not from auth cookie claims, so a future permission revocation takes effect immediately rather than waiting on cookie/claim refresh) and passes it on every PictureService call. `PictureServiceClient`/`CollectionQueryService` move from singleton to per-circuit (scoped) so they can carry this.
- Add a collection indicator to the Web navigation, structured so a future multi-collection selector can be added without redesign — in this change every user has exactly one accessible collection (their own), so it never presents a choice.
- One-time data migration: assign all pre-existing S3/DynamoDB data (which predates the Collection concept and has no owner) to a Collection owned by the existing user "anton".
- CatalogService is unaffected — it holds only shared reference data with no per-user concept.

## Capabilities

### New Capabilities
- `web-collections`: Collection and Collection Membership model, automatic Collection creation at registration, live per-request Owner-role authorization, and the Web navigation's current-collection indicator.
- `collection-scoped-picture-access` (unprefixed — cross-cutting contract between Web and PictureService): the shared rule that every collection-scoped PictureService RPC requires a `collection_id`, that data written under one collection is never returned by an operation scoped to another, and that PictureService validates a `collection_id`'s existence but does not independently re-authorize the caller (Web is the sole caller, reachable only over the private network, and is responsible for authorization).

### Modified Capabilities
- `picture-service-photo-storage`: photo bytes and sidecar records are now stored keyed by `collection_id` in addition to their existing identifier; adds a one-time migration requirement that assigns all pre-existing (pre-Collection) data to a specific collection.
- `picture-service-photo-upload`: `UploadPhoto` now stores the uploaded photo within the collection specified by the caller.
- `picture-service-card-listing`: `ListCards` now reports cards within one given collection rather than across all stored photos.
- `picture-service-sidecar-cache`: the in-memory sidecar cache is now keyed by `(collection_id, photo_id)` instead of `photo_id` alone.
- `picture-service-photo-scan`: `Scan` now batch-processes photos within one given collection rather than across all stored photos.
- `picture-service-photo-download`: resolving a photo's download URL now requires the collection it belongs to.
- `picture-service-photo-deletion`: `DeletePhoto` now requires the collection the photo belongs to.
- `picture-service-sidecar-editing`: all five sidecar-editing RPCs (`UpdateSidecar`, `UpdateSetName`, `UpdateCardNumber`, `UpdateCardLanguage`, `UpdateReviewStatus`) now require the collection the photo belongs to.

## Impact

- **NinjagoScanner.Web**: new `Collections`/`CollectionMemberships` tables in `AppDbContext` (same SQLite file as `AspNetUsers`); registration flow creates a Collection + Owner membership alongside the `AppUser`; a new scoped "current collection" resolution service backed by a live membership query; `PictureServiceClient` and `CollectionQueryService` change from `AddSingleton` to scoped (the underlying shared `GrpcChannel` stays a singleton dependency, preserving `web-grpc-client-connection-reuse`'s one-channel-per-service guarantee); nav gains a current-collection indicator.
- **NinjagoScanner.PictureService**: `Protos/picture_service.proto` gains a `collection_id` field on `ScanRequest`, `UploadPhotoMetadata`, `ListCardsRequest`, `GetPhotoDownloadUrlRequest`, `GetCardDetailsRequest`, `UpdateSidecarRequest`, `UpdateSetNameRequest`, `UpdateCardNumberRequest`, `UpdateCardLanguageRequest`, `UpdateReviewStatusRequest`, and `DeletePhotoRequest`; `PhotoStore.cs` (S3 key scheme), `SidecarTable.cs` (DynamoDB key schema — new table), and `SidecarCache.cs` (cache key) all change; `ListCards`/`Scan` move from full bucket/table scans to collection-scoped queries. `MigrateSidecars` (the existing legacy-status-key repair operation) stays a global, unscoped maintenance RPC — it repairs storage format across all data regardless of collection and is not part of the app's per-user surface, so it is deliberately left out of the `collection_id` requirement.
- **NinjagoScanner.CatalogService**: no changes.
- **Data migration**: a one-time script assigns all existing S3 objects and DynamoDB items (currently collection-less) to a new Collection owned by the existing user "anton".
- **openspec/GLOSSARY.md**: needs new entries for Collection, Collection Membership, Owner, and Reader — tracked as a follow-up rather than in this change's scope.
