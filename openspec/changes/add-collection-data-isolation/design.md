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

`collection_id` is resolved once per circuit (from `HttpContext.User` + a live membership query) inside the scoped wrapper and attached to every outgoing request internally — public C# method signatures on `PictureServiceClient`/`CollectionQueryService` do not gain a new parameter, so the ~20 existing call sites in Razor page code are unaffected.

### `MigrateSidecars` stays a global, unscoped operation
It repairs a legacy on-disk/DynamoDB record shape (missing `AnalysisStatus` key), not a per-user product path — it's described in the proto as an admin/recovery operation. Scoping it per-collection would mean running it once per collection to fully repair the data set, which works against its purpose as a one-shot safety net. It is deliberately excluded from the `collection_id` requirement in `collection-scoped-picture-access`.

### `GetCardDetails` is covered by the cross-cutting spec, not a dedicated capability delta
No existing `openspec/specs/` capability documents `GetCardDetails` today (a pre-existing gap, not something this change is trying to fully backfill). It still gains a `collection_id` field and is still covered by `collection-scoped-picture-access`'s blanket "every RPC that reads or writes photo or sidecar data" requirement; it just has no dedicated modified-capability file to attach a more specific delta to.

## Migration Plan

1. **Schema migration (Web)**: add `Collections` and `CollectionMemberships` tables to `AppDbContext`. Since the project currently relies on `EnsureCreated()` rather than EF migrations, and this is the first schema change since login shipped, introduce a real EF Core migration here rather than extending `EnsureCreated()` further — `EnsureCreated()` cannot evolve an existing database's schema in place.
2. **Backfill collections for existing users**: for "anton" (and any other pre-existing account), create a Collection + `Owner` membership. New registrations get this automatically going forward (per `web-collections`).
3. **PictureService storage cutover**:
   a. Create the new DynamoDB table (`CollectionId` partition key, `PhotoId` sort key).
   b. Copy every existing sidecar item into the new table under anton's `collection_id`.
   c. Copy every existing S3 object from `photos/<photo_id>` to `photos/<collection_id>/<photo_id>` under anton's `collection_id`.
   d. Cut PictureService over to the new table/key scheme; retire the old table once the copy is verified.
4. **Deploy order**: PictureService's proto/storage changes and Web's collection-aware calls must ship together (the `collection_id` field is newly required, so an old Web calling a new PictureService — or vice versa — breaks). Given both are redeployed as part of one Fly deployment sequence, take PictureService and Web through a coordinated deploy rather than independent rollouts for this change.
5. **Rollback**: keep the old DynamoDB table and S3 prefix untouched until the new scheme is verified in production (matches the existing `picture-service-photo-storage` "one-time migration preserves local originals" precedent of never deleting the source during a migration). If a rollback is needed, PictureService can be redeployed against the old table/prefix since nothing is deleted during cutover.

## Risks / Trade-offs

- **[Risk] Coordinated deploy requirement**: PictureService and Web must move together since the proto field becomes required — a partial deployment breaks calls in either direction. → **Mitigation**: treat this as one coordinated release; there is no need for the field to be `optional`/backward-compatible since both services are operated together.
- **[Risk] DynamoDB table cutover is a one-way data copy, not a live migration**: a photo uploaded between the copy and the cutover could be missed. → **Mitigation**: perform the copy during a short maintenance window (stop PictureService, copy, redeploy) rather than attempting a live dual-write; acceptable given this is a low-traffic personal app.
- **[Trade-off] PictureService trusts Web's authorization decision without independent verification**: a bug in Web's membership check could let a user address a `collection_id` they don't own. → Accepted per the decided trust boundary (PictureService has no public endpoint; Web is the only caller) — documented explicitly in `collection-scoped-picture-access` rather than silently assumed.
- **[Trade-off] `MigrateSidecars` remaining unscoped** means it still touches every collection's data in one call — consistent with the isolation goal, since it repairs storage format rather than exposing data to any collection's users.
