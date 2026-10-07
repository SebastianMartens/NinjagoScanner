## 1. PictureService photo transfer (Python)

- [x] 1.1 Add `TransferPhotos` messages/RPC to `NinjagoScanner.Web/Protos/picture_service.proto`; verify `uv run python scripts/gen_proto.py` and `dotnet build NinjagoScanner.slnx` succeed
- [x] 1.2 Add `PhotoStore.copy` (S3 copy within bucket) and verify with a unit test using the existing S3 fake
- [x] 1.3 Add sidecar transfer helpers in `sidecar_table.py`/`sidecar_store.py`/`sidecar_cache.py` (read, create under new ID, delete, cache invalidation for both collections) and verify with pytest
- [x] 1.4 Implement `TransferPhotos` in `picture_scanner_service.py` with validation, copy -> write -> verify -> delete ordering, compensation on failure, and idempotent transfer records; verify pytest covers success, unknown photo, same collection, unknown collection, mid-transfer failure rollback, idempotent retry, cache consistency
- [x] 1.5 Run full `uv run pytest` and verify green

## 2. Web data model

- [x] 2.1 Add `Friendship`, `CollectionSharingSettings`, `Trade`, `TradeItem`, `TradeLogEntry`, `TradeLogItem` entities and `AppDbContext` configuration (unordered-pair unique index, concurrency token on `Trade`); verify build
- [x] 2.2 Add EF migration `AddFriendsAndTrading` and verify it applies to a fresh SQLite database in a test

## 3. Friends

- [x] 3.1 Implement `FriendService` (search, request, accept, decline, cancel, remove, mutual-request resolution, list) with xunit tests for every scenario in `web-friends`
- [ ] 3.2 Build `/friends` page (German UI, auth required, nav entry with pending badge) and verify with a bUnit/host test and a manual browser check

## 4. Collection sharing

- [x] 4.1 Implement `FriendAccessService` and visibility setting with tests for friend, non-friend, private, removed friend, forged username
- [x] 4.2 Add read-only friend facade over `CollectionQueryService` and `GamificationService` (no unlock/celebration side effects); verify tests that no write RPC is reachable for foreign collections
- [ ] 4.3 Build `/friends/{username}` page (overview, gallery, rank/XP, achievements, comparison counts, visibility toggle in own settings) and verify via browser check

## 5. Trade finder

- [x] 5.1 Implement pure `TradeMatchingService` (surplus, wanted, tier pairing, weight fallback, `Unausgewogen` flag, best-copy retention) with unit tests for every scenario in `web-trade-finder`
- [x] 5.2 Implement partner ranking over visible friends and verify ordering/zero-overlap/private-excluded tests
- [ ] 5.3 Build `/trade` finder UI (partner list, suggestion with manual adjust, balance indicator) and verify via browser check

## 6. Trade execution and log

- [x] 6.1 Implement `TradeService` propose/decline/cancel with validation and reservation rules; verify tests
- [x] 6.2 Implement accept/execute (revalidate, optimistic status flip, `TransferPhotos`, completion transaction with log + BonusXp) and verify tests for success, stale, transfer failure, double accept, XP once, sidecar preserved
- [x] 6.3 Add recovery sweep for stale `Executing` trades and verify test
- [x] 6.4 Extend `PictureServiceTestHost` fake with real move semantics and add end-to-end trade tests through in-process hosts
- [ ] 6.5 Build trade proposal/inbox UI and `/trade/log` page (only own trades, immutable) and verify via browser check

## 7. Finish

- [x] 7.1 Update `openspec/GLOSSARY.md` (Friend, Friendship, Trade, Trade Log) and CLAUDE.md architecture notes
- [x] 7.2 Run `dotnet test NinjagoScanner.slnx` and `uv run pytest`; verify both green
- [x] 7.3 `openspec validate friends-and-trading --strict` passes
