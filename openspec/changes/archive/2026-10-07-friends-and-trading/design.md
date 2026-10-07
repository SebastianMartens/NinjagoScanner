## Context

Users, collections and gamification state live in Web's SQLite database (EF Core, Identity `AppUser`, `Collection`, `CollectionMembership`, `GamificationProfile.BonusXp`). Photos live in S3 (`photos/<collection_id>/<photo_id>`) and sidecars in DynamoDB, both owned by PictureService, which is collection-scoped by ID and trusts Web for authorization (see `collection-scoped-picture-access`). Rarity is catalog data (CatalogService). Owned copies per card are computed by `CollectionQueryService` from sidecars + catalog. See proposal.md for motivation.

Constraint: a trade spans two stores (SQLite + S3/DynamoDB) that cannot share a transaction.

## Goals / Non-Goals

**Goals:** friends, read-only sharing, rarity-balanced trade suggestions, trades that never lose or duplicate a card, a durable log.

**Non-Goals:** public profiles, chat/notifications (in-app badge only), trading with non-friends, trading unmapped photos, money/marketplace, trade counter-offers (decline and re-propose instead), real-time push.

## Decisions

**D1 - Friendship model.** One `Friendship` row per pair: `(RequesterUserId, AddresseeUserId, Status{Pending,Accepted}, CreatedAt, RespondedAt)`, with a unique index on the unordered pair (store `UserLowId/UserHighId` canonical columns). Decline/cancel/remove delete the row. *Alternative:* two directed rows per friendship - rejected, doubles invariants.

**D2 - Sharing authorization lives in one service.** `FriendAccessService.ResolveVisibleCollectionAsync(viewer, username)` returns the friend's collection ID only for accepted friend + `Freunde` visibility, else null. Pages never receive a raw collection ID from the client. Visibility stored in `CollectionSharingSettings(CollectionId, Visibility)`; missing row = `Freunde`. *Alternative:* per-field visibility - YAGNI.

**D3 - Reuse existing query code for friend views.** `CollectionQueryService` already takes a collection ID in its PictureService calls; friend views call it with the resolved ID through a read-only facade so no write RPC is reachable. Gamification for a friend is computed with the same `GamificationService` read path (no unlock side effects/celebrations for the viewer).

**D4 - Trade matching is pure and deterministic.** `TradeMatchingService` takes two owned-copy maps (catalog card -> list of photo IDs, rarity) and returns suggestions; no I/O, so it is unit-testable. Surplus = copies - 1. Algorithm: group give/receive candidates by rarity tier; pair same tier first; leftover pairs matched by weight (common 1, limited 3, legendary 9) minimizing |weight difference|, greedy by tier descending; pair difference > one tier flagged `Unausgewogen`. Equal counts on both sides is a hard invariant. Photo choice among duplicates: the photo with the worst quality (unreviewed/`uncertain` first, `verified` last) is offered first so the owner keeps the best copy.

**D5 - Trade state machine.** `Trade(Id, ProposerCollectionId, RecipientCollectionId, ProposerUserId, RecipientUserId, Status{Pending,Completed,Declined,Cancelled,Failed}, CreatedAt, ResolvedAt)` and `TradeItem(TradeId, Side, PhotoId, SeriesName, CardNumber, CardName, Rarity)` (card snapshot at proposal). A photo reserved in a pending trade is excluded from new offers by query. Executing: Web loads the trade, re-validates against fresh PictureService data, flips Status Pending->Executing using an optimistic concurrency token (so double-accept moves once), calls `TransferPhotos` per direction, then in ONE SQLite transaction sets Completed, writes `TradeLogEntry`+items, and adds `BonusXp`. If the transfer fails Status becomes Failed (and PictureService has compensated).

**D6 - Cross-store atomicity via PictureService transfer with compensation.** `TransferPhotos(source, dest, photo_ids, transfer_id)`: copy each object and write each destination sidecar first (new photo IDs), verify all, then delete sources. On any error delete created destination objects/sidecars, leave source untouched. A trade needs two transfers (A->B, B->A); Web orchestrates them: transfer 1, transfer 2, and if transfer 2 fails, reverse transfer 1 using the returned ID mapping (a compensating `TransferPhotos`). Residual risk (process dies between steps) is handled by D7. *Alternative:* a single bidirectional RPC - chosen instead: `TransferPhotos` accepts a list of moves `(source, dest, photo_id)` so both directions are one call and one compensation scope; this is the final contract. DynamoDB `TransactWriteItems` is used for sidecar create/delete when available within 100 items; S3 is not transactional so ordering (copy -> write sidecars -> delete) is what guarantees no loss.

**D7 - Idempotency and recovery.** `transfer_id` = trade ID; PictureService stores a small completed-transfer record (DynamoDB item `TRANSFER#<id>` in the sidecar table, with TTL) holding the old->new mapping, making retries idempotent. A trade left `Executing` for more than 5 minutes is retried by a startup/hosted-service sweep calling `TransferPhotos` again with the same ID, then finalizing.

**D8 - Trade log.** `TradeLogEntry(TradeId unique, CompletedAtUtc)` and `TradeLogItem` rows hold username snapshots and card snapshots; written in the completion transaction (D5). No update/delete paths exist in the service layer. Declined/cancelled/failed trades are visible in history through `Trade.Status` but are not log entries.

**D9 - Proto.** `TransferPhotos(TransferPhotosRequest{transfer_id, repeated PhotoMove{source_collection_id, dest_collection_id, photo_id}}) -> TransferPhotosResponse{repeated PhotoMoveResult{old_photo_id, new_photo_id, source_collection_id, dest_collection_id}}` in `NinjagoScanner.Web/Protos/picture_service.proto`; `PictureServiceTestHost` fake implements it with real in-memory move semantics.

**D10 - UI.** Blazor pages in `Components/Pages` (`Friends.razor`, `FriendCollection.razor`, `Trade.razor`, `TradeLog.razor`), explicit `@rendermode InteractiveServer` consistent with existing pages, German text with Umlauts, nav entries in `Components/Layout`. Pending incoming requests/trades show a count badge in the nav.

## Risks / Trade-offs

- [Process crash mid-trade leaves cards moved one way] -> D6 ordering + D7 retry sweep; the Executing status makes it observable.
- [Friend view leaks data] -> D2 single choke point plus tests for non-friend, private, removed friend and forged username.
- [Race: owner deletes/edits a photo while in pending trade] -> execution re-validates; stale trades fail cleanly (spec).
- [Rarity balance disputes] -> balance is advisory (flagged), both sides must explicitly accept.
- [New photo IDs on transfer break external references] -> none exist besides sidecar/S3; photos are only referenced via Web's live reads.
- [Username enumeration] -> search needs min 2 chars, signed-in only, rate-limited by existing auth rate limiting.

## Migration Plan

EF migration `AddFriendsAndTrading` (additive tables only). No data backfill: missing sharing settings = `Freunde`. PictureService deploys first (new RPC is additive), then Web. Rollback: Web rollback leaves unused tables; completed trades remain valid data.
