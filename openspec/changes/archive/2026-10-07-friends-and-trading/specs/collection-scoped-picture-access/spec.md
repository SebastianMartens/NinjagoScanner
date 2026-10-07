## ADDED Requirements

### Requirement: Web authorizes friend reads and trades before touching a foreign collection
Because PictureService has no membership check, Web SHALL call a collection-scoped RPC with a collection ID other than the acting user's own only for (a) read-only RPCs serving a verified friend view, or (b) `TransferPhotos` for a trade validated by Web. All other calls SHALL carry the acting user's own collection.

#### Scenario: Friend read
- **WHEN** a friend view loads another user's cards
- **THEN** only read RPCs are called with that user's collection ID

#### Scenario: No foreign writes
- **WHEN** a user acts on a friend's collection outside a trade
- **THEN** Web makes no write RPC for it
