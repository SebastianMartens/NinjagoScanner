## Purpose

Lets a user's accepted friends view the user's collection and achievements read-only, under a visibility setting the owner controls.

## ADDED Requirements

### Requirement: Visibility setting
Each collection owner SHALL have a visibility setting for their collection and achievements with values `Nur ich` (private) and `Freunde` (accepted friends). The default for existing and new collections SHALL be `Freunde`. Data SHALL never be visible to non-friends.

#### Scenario: Default visibility
- **WHEN** a new user registers
- **THEN** their visibility is `Freunde`

#### Scenario: Owner sets private
- **WHEN** the owner sets visibility to `Nur ich`
- **THEN** friends can no longer view collection or achievements, but remain friends

### Requirement: Friend collection view is read-only
An accepted friend SHALL be able to open `/friends/{username}` and see that user's overview (owned/missing counts, series progress), gallery of owned cards, rank, XP and unlocked achievements. The view SHALL offer no edit, upload, review, delete or rotation actions, and SHALL NOT expose sidecar details beyond what the gallery shows.

#### Scenario: Friend views collection
- **WHEN** B is an accepted friend of A and A's visibility is `Freunde`
- **THEN** B sees A's owned cards, rank, XP and achievements read-only

#### Scenario: Non-friend denied
- **WHEN** C is not an accepted friend of A
- **THEN** `/friends/A` responds as not found and no data of A is returned

#### Scenario: Private collection denied
- **WHEN** A's visibility is `Nur ich` and a friend opens `/friends/A`
- **THEN** the friend sees a notice that the collection is private and no collection data

#### Scenario: No mutating actions
- **WHEN** a friend views another user's collection
- **THEN** no control or endpoint lets them modify that collection's photos or sidecars

### Requirement: Server-side enforcement of friendship for foreign collection reads
Web SHALL resolve a foreign collection ID for PictureService calls only after verifying an accepted friendship and visibility permission on the server for that request; the collection ID SHALL never be taken from client input directly.

#### Scenario: Forged collection id
- **WHEN** a user supplies another user's collection or username without friendship
- **THEN** Web makes no PictureService call for that collection

### Requirement: Compare with friend
The friend view SHALL show how the friend's collection compares to the viewer's: cards both own, cards only the friend owns, and cards only the viewer owns.

#### Scenario: Comparison counts
- **WHEN** a friend view is opened
- **THEN** the counts of shared, friend-only and viewer-only cards are displayed
