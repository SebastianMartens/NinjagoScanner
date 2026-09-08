## Context

See `proposal.md` — Why/What Changes for motivation and scope. This section covers only the current-state facts that shape the approach.

- Web's SQLite (`AppDbContext : IdentityDbContext<AppUser>`) already persists `AppUser` and is created via `db.Database.EnsureCreated()` at startup (no EF migrations exist yet).
- `PictureServiceClient` and `CollectionQueryService` are `AddSingleton`, built once at startup around one shared `GrpcChannel`. There is no `IHttpContextAccessor` anywhere in `NinjagoScanner.Web` today.
- PictureService's DynamoDB sidecar table has `PhotoId` as its only key (hash key, no sort key); S3 objects are keyed `photos/<photo_id>`. Both need a new key shape; DynamoDB partition keys are immutable, so this is a new table plus a data copy, not an alter.
- An existing registered user, "anton", currently has (implicitly) all of the pre-existing photo/sidecar data.
- PictureService has no public Fly IP (private 6PN network only); Web is its only caller.

## Goals / Non-Goals

**Goals:**
- Every photo/sidecar read or write is scoped to one `collection_id`, enforced at the PictureService storage layer (S3 key, DynamoDB key), not just filtered in Web.
- Web authorizes (live SQLite membership check, Owner role only); PictureService isolates (never crosses collection boundaries) and validates existence, not identity.
- No behavior change to unrelated capabilities: CatalogService, and `web-grpc-client-connection-reuse`'s one-channel-per-service guarantee.

**Non-Goals:**
- Collection management UI (create/rename/delete additional collections).
- Sharing UI or a working `Reader` role — the `Role` column exists and accepts `Reader`, but no code path grants it or checks it differently from "no membership."
- Cross-collection browsing (e.g. future "browse public collections to find cards to share").
- Any change to `GetPhotoDownloadUrl`'s pre-signed URL mechanism itself, or to who can use a URL once issued (decided: not sensitive, no extra checks after issuance).

## Decisions

### Collections/Memberships live in Web's SQLite, not DynamoDB
Ownership and sharing are relational (a Collection has one Owner and, later, N Readers); that's a natural fit for a SQL join table and a poor fit for DynamoDB's scan/GSI model. It also keeps PictureService free of any concept of "user" — it only ever sees an opaque `collection_id`. Alternative considered: a `Collections`/`CollectionMemberships` pair in DynamoDB, colocated with PictureService's other data — rejected because it would require PictureService to understand accounts and roles, and because the discussed future need (browsing/filtering collections by visibility) is exactly the kind of query SQL handles better.

### `Collections` has no `OwnerId` column
Ownership is just a `CollectionMemberships` row with `Role = Owner`. A separate `OwnerId` column would be a second place that could disagree with the membership table about who owns what. `Collections` holds only `Id`, `Name`, `CreatedAt`.

### Authorization is a live SQLite query per request, not ASP.NET claims
Claims baked into the auth cookie only refresh on `RevalidatingIdentityAuthenticationStateProvider`'s 30-minute cycle (or at next login). Since a collection is a privacy boundary between people's data, a revoked share should take effect immediately rather than up to 30 minutes later. The live-query cost is a single indexed SQLite lookup per request — not a meaningful performance concern at this scale. Claims remain fine for coarse authentication state ("is this a valid session"), just not for collection-level authorization.

### Explicit `collection_id` proto field, not a gRPC interceptor/metadata header
An interceptor would centralize the plumbing in two places (one client interceptor in Web, one server interceptor in PictureService) instead of touching every message, but it makes the scoping invisible in the `.proto` contract itself and couples the mechanism to gRPC metadata specifically. The explicit field is more mechanical to add (touches ~11 request messages) but keeps the contract self-documenting and technology-independent. This was a deliberate trade-off the user made in favor of simplicity/visibility over touch count.

### `PictureServiceClient`/`CollectionQueryService` become scoped; `GrpcChannel` stays a singleton
Only the identity-aware wrapper needs per-circuit lifetime (to hold the resolved `collection_id` for that circuit); the underlying channel is still safe and intended to be shared/reused. This split preserves `web-grpc-client-connection-reuse`'s "one channel per target service" guarantee — the channel becomes a singleton dependency injected into the now-scoped client wrapper, rather than each wrapper opening its own channel. `CatalogServiceClient` is unaffected and stays a singleton (no user-scoped data).

`collection_id` is resolved once per circuit inside the scoped wrapper and attached to every outgoing request internally — public C# method signatures on `PictureServiceClient`/`CollectionQueryService` do not gain a new parameter, so the ~20 existing call sites in Razor page code are unaffected. The wrapper obtains the acting `ClaimsPrincipal` via `AuthenticationStateProvider.GetAuthenticationStateAsync()` rather than `HttpContext.User` — `HttpContext` isn't reliably available for a Blazor Server circuit's full lifetime (only during its initial request), while `AuthenticationStateProvider` is the framework's own mechanism for this and is already used in this codebase (`RevalidatingIdentityAuthenticationStateProvider`). The page-level Owner-role gate (a custom `AuthorizationHandler`) instead reads `AuthorizationHandlerContext.User` directly, which ASP.NET Core populates correctly whether the check runs via HTTP authorization middleware or Blazor's `AuthorizeRouteView` — so no `IHttpContextAccessor` registration is needed anywhere.

### `MigrateSidecars` stays a global, unscoped operation
It repairs a legacy on-disk/DynamoDB record shape (missing `AnalysisStatus` key), not a per-user product path — it's described in the proto as an admin/recovery operation. Scoping it per-collection would mean running it once per collection to fully repair the data set, which works against its purpose as a one-shot safety net. It is deliberately excluded from the `collection_id` requirement in `collection-scoped-picture-access`.

### `GetCardDetails` is covered by the cross-cutting spec, not a dedicated capability delta
No existing `openspec/specs/` capability documents `GetCardDetails` today (a pre-existing gap, not something this change is trying to fully backfill). It still gains a `collection_id` field and is still covered by `collection-scoped-picture-access`'s blanket "every RPC that reads or writes photo or sidecar data" requirement; it just has no dedicated modified-capability file to attach a more specific delta to.

## Migration Plan

Steps are strictly ordered — later steps depend on earlier ones having actually run in production, not just merged in code (this bit us once already: see the note on step 2).

1. **Infra**: `terraform apply` a new DynamoDB table (`CollectionId` partition key, `PhotoId` sort key — a genuinely new resource, not an edit to the existing `PhotoId`-only table, since DynamoDB's hash/range key is immutable and an in-place edit would make Terraform destroy-and-recreate the table, deleting the data before it could be copied) and add `dynamodb:Query` to PictureService's IAM policy (the new `SidecarTable.ListByCollectionAsync` queries by partition key instead of scanning). No S3 change needed — a bucket has no key schema, so the existing bucket takes the new `photos/<collection_id>/<photo_id>` keys as-is alongside the old `photos/<photo_id>` ones; only the application code's key convention changes.
2. **Deploy the new Web code (alone, first)**. Its EF Core migration (`Collections`/`CollectionMemberships` — see the schema note below) only runs when the app actually starts (`Program.cs`'s `db.Database.Migrate()` at startup), not at merge/build time — a database copy pulled before this deploy won't have the new tables yet, which is exactly what happened when we first tried the migration tool's dry run against a pre-deploy copy. **This deploy has a real user-facing side effect**: every existing account is immediately locked out (`CollectionOwnerRequirement` denies anyone with no `Owner` membership yet, and only new registrations get one automatically) until step 3 completes. Treat steps 2–4 as one continuous maintenance window, not independent deploys with idle time in between.
   - Schema migration note: the project relies on `EnsureCreated()` rather than EF migrations today, and this is the first schema change since login shipped, so this introduces a real EF Core migration rather than extending `EnsureCreated()` further — `EnsureCreated()` cannot evolve an existing database's schema in place. Because every existing deployment's `users.db` was created via `EnsureCreated()` (no `__EFMigrationsHistory` table, Identity tables already present), the migration's `Up()` creates the Identity tables via idempotent raw SQL (`CREATE TABLE/INDEX IF NOT EXISTS`) instead of EF's typed `CreateTable`/`CreateIndex` — a no-op against an existing database, a normal create against a brand new one — while `Collections`/`CollectionMemberships` (genuinely new either way) use the normal typed API. Verified directly against the local dev `users.db` (an `EnsureCreated()`-origin database): existing account preserved, new tables created, Identity tables untouched.
3. **Run the data migration tool** (`NinjagoScanner.CollectionAssignmentMigration`) against a copy of the now-migrated production `users.db` (pulled *after* step 2, not before) plus the old and new DynamoDB tables and the (single, shared) S3 bucket: creates anton's `Collection` + `Owner` membership, copies every existing sidecar item into the new table under that `collection_id`, and copies every existing S3 object from `photos/<photo_id>` to `photos/<collection_id>/<photo_id>`. Dry-run first. Push the updated `users.db` back to the Fly volume.
4. **Deploy PictureService**, with `Storage__SidecarTableName` switched to the new table's name (same bucket name, no bucket change) — this is the point at which `collection_id` becomes required end-to-end, and anton's data (now present under his `collection_id`) becomes reachable again.
5. **Verify**: anton logs in and sees all pre-existing cards; other accounts see nothing; a fresh registration gets its own empty collection; upload/scan/review/delete all still work.
6. **Rollback**: keep the old DynamoDB table and un-prefixed S3 objects untouched until the new scheme is verified in production (matches the existing `picture-service-photo-storage` "one-time migration preserves local originals" precedent of never deleting the source during a migration). If a rollback is needed, point `Storage__SidecarTableName` back at the old table and redeploy PictureService, since nothing is deleted during cutover. Decommission the old table/objects later, once confident, as a separate follow-up.

## Risks / Trade-offs

- **[Risk] Coordinated deploy requirement, with a specific required order**: PictureService and Web must move together since the proto field becomes required, and per the Migration Plan above the order is fixed (Web first, then the data migration tool, then PictureService) — Web's deploy is what creates the tables the migration tool needs, and PictureService's deploy is what makes migrated data reachable again. Deploying PictureService before the migration tool has run would leave it pointed at an empty new table; running the migration tool before Web's deploy fails outright (no `Collections`/`CollectionMemberships` tables yet to write to — see the Migration Plan step 2 note). → **Mitigation**: follow the Migration Plan's step order exactly; there is no need for the field to be `optional`/backward-compatible since both services are operated together within one maintenance window.
- **[Risk] DynamoDB table cutover is a one-way data copy, not a live migration**: a photo uploaded between the copy and the cutover could be missed. → **Mitigation**: perform the copy during a short maintenance window (stop PictureService, copy, redeploy) rather than attempting a live dual-write; acceptable given this is a low-traffic personal app.
- **[Trade-off] PictureService trusts Web's authorization decision without independent verification**: a bug in Web's membership check could let a user address a `collection_id` they don't own. → Accepted per the decided trust boundary (PictureService has no public endpoint; Web is the only caller) — documented explicitly in `collection-scoped-picture-access` rather than silently assumed.
- **[Trade-off] `MigrateSidecars` remaining unscoped** means it still touches every collection's data in one call — consistent with the isolation goal, since it repairs storage format rather than exposing data to any collection's users.
