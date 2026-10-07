## Purpose

Defines how friends propose, accept, decline and execute card trades so that cards actually move between collections without loss or duplication.

## ADDED Requirements

### Requirement: Trade proposal
A user SHALL be able to propose a trade to an accepted friend consisting of one or more specific card photos from their own collection and one or more from the friend's collection, with equal numbers of cards on both sides. The proposal is `pending` until the recipient accepts or declines or the proposer cancels.

#### Scenario: Valid proposal
- **WHEN** A selects 2 of their duplicate photos and 2 of B's duplicate photos that A is missing and proposes
- **THEN** a pending trade is created and appears for B

#### Scenario: Not friends
- **WHEN** a trade is proposed to a non-friend
- **THEN** it is rejected

#### Scenario: Unequal counts
- **WHEN** the two sides have different numbers of cards
- **THEN** the proposal is rejected

#### Scenario: Non-tradable card
- **WHEN** a selected photo is not a surplus copy (it is the owner's last copy of that card, or unmapped)
- **THEN** the proposal is rejected

### Requirement: Accept executes the trade atomically
When the recipient accepts, the system SHALL re-validate every card (still owned by its side, still a surplus copy, friendship still exists) and then move each photo with its sidecar to the other collection. Either all cards move or none do; on failure the pending trade remains unexecuted with no collection changed, and the user is told in German.

#### Scenario: Successful trade
- **WHEN** B accepts a valid trade
- **THEN** each offered photo and its sidecar belong to the other collection, no card exists in both or neither, and the trade is `completed`

#### Scenario: Stale trade
- **WHEN** one offered photo was deleted or traded elsewhere before acceptance
- **THEN** the trade fails as `invalid`, nothing moves, and both users are informed

#### Scenario: Partial failure rolled back
- **WHEN** moving the third of four cards fails
- **THEN** the already moved cards are returned and no card is lost or duplicated

#### Scenario: Double accept
- **WHEN** accept is submitted twice concurrently
- **THEN** cards move exactly once

### Requirement: Moved cards keep their data
A moved photo SHALL keep its AI analysis, sidecar fields, review status, language and rotation, and SHALL receive a new photo ID in the receiving collection. The receiving collection's counts and gamification SHALL reflect the cards on next read.

#### Scenario: Sidecar preserved
- **WHEN** a photo with Review Status `verified` is traded
- **THEN** it arrives as `verified` with the same card mapping

### Requirement: Decline and cancel
The recipient SHALL be able to decline and the proposer SHALL be able to cancel a pending trade; no cards move, and the trade is closed.

#### Scenario: Decline
- **WHEN** B declines
- **THEN** the trade is `declined` and both collections are unchanged

### Requirement: Cards in pending trades are reserved
A photo offered in a pending trade SHALL NOT be offered in another pending trade at the same time.

#### Scenario: Double offer
- **WHEN** a photo already in a pending trade is added to a second proposal
- **THEN** the second proposal is rejected

### Requirement: Trade XP
Each participant of a completed trade SHALL receive 25 bonus XP once per completed trade.

#### Scenario: XP granted once
- **WHEN** a trade completes
- **THEN** both participants gain 25 bonus XP, and repeating the accept does not grant more
