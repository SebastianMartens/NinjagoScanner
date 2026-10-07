## Why

Today every collection is an island: only its owner can see it, and the duplicate cards that pile up in one collection can never fill the gaps in another. Collectors want to connect with friends, show off their collection and achievements, and swap duplicates against missing cards - the core social loop of any card hobby.

## What Changes

- Users can find other users by username, send a friend request, and accept/decline/cancel it; either side can remove a friendship later.
- Friends can view each other's collection (overview tiles, gallery, series progress) and gamification state (rank, XP, unlocked achievements) read-only. Each owner has a visibility setting (`Nur ich` / `Freunde`) for collection and achievements; default is `Freunde`-visible for accepted friends only, never public.
- A trade finder compares two friends' collections and proposes swaps: my duplicate cards (owned copies > 1) against cards the friend is missing, and vice versa, balanced by catalog **Rarity** (`common` < `limited` < `legendary`) so both sides give and get comparable value. A "Tauschpartner finden" view ranks friends by how much they can trade with me.
- A user can propose a concrete trade to a friend, the friend accepts/declines/the proposer cancels. Accepting executes the trade: the card photos (S3 object + sidecar) physically move from one collection to the other.
- Every executed trade is written to an immutable trade log visible to both participants (`Tauschprotokoll`), listing who gave and received which cards and when.
- A new PictureService RPC `TransferPhotos` moves photo bytes and sidecars between collections atomically-enough (copy, write, then delete source, with compensation on failure) and is the only place cross-collection moves happen.
- Trading awards the reserved `GamificationProfile.BonusXp` (trade XP) to both participants.
- New German UI pages: `/friends`, `/friends/{username}` (friend's collection + achievements), `/trade` (finder + proposals), `/trade/log`.

## Capabilities

### New Capabilities
- `web-friends`: friend search, requests, accept/decline/remove, friend list.
- `web-collection-sharing`: friend-visible read-only collection and achievements views plus the visibility setting.
- `web-trade-finder`: duplicate-vs-missing matching between friends with rarity balancing and partner ranking.
- `web-trade-execution`: trade proposal lifecycle (propose/accept/decline/cancel), execution, validation, trade XP.
- `web-trade-log`: immutable per-trade log and its UI.
- `picture-service-photo-transfer`: `TransferPhotos` RPC moving photos and sidecars between collections.

### Modified Capabilities
- `collection-scoped-picture-access`: reading another collection's photo data is allowed for a caller-asserted friend read (Web enforces friendship; PictureService stays collection-scoped by ID).
- `web-rank-progression`: trade XP contributes through `BonusXp`.

## Impact

- **NinjagoScanner.Web**: new EF entities (`Friendship`, `CollectionSharingSettings`, `Trade`, `TradeItem`, `TradeLogEntry`) + migration; new services (`FriendService`, `TradeMatchingService`, `TradeService`), new Blazor pages, nav entries; `GamificationService` reads `BonusXp`.
- **Cross-cutting**: `picture_service.proto` (canonical copy in Web) gains `TransferPhotos` - touches Web, PictureService and the test fakes (`PictureServiceTestHost`).
- **picture_service/**: `photo_store.py`, `sidecar_store.py`/`sidecar_table.py`/`sidecar_cache.py` gain a transfer path; cache invalidation for both collections.
- **Tests**: Web.Tests (matching, atomicity, privacy), picture_service pytest (transfer, rollback).
- **Glossary**: new terms Friend, Friendship, Trade, Trade Offer, Trade Log.
