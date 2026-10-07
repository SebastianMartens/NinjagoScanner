# web-trade-log Specification

## Purpose

Gives both trading partners a permanent record of every executed trade and every trade attempt outcome.

## Requirements

### Requirement: Log entry per completed trade
Every completed trade SHALL create exactly one immutable log entry recording both users, the timestamp (UTC), and for each moved card its series, card number, name, rarity and direction. The entry SHALL be written in the same unit of work as the trade's completion state, so a completed trade always has a log entry and vice versa.

#### Scenario: Entry written
- **WHEN** a trade completes
- **THEN** one log entry exists listing all cards given and received by each side

#### Scenario: No entry without completion
- **WHEN** a trade fails or is declined
- **THEN** no completed-trade entry exists (the outcome is still shown in the user's trade history as declined/cancelled/failed)

#### Scenario: Snapshot survives later changes
- **WHEN** a friendship is removed or a card is later deleted
- **THEN** the log entry still shows the same card data

### Requirement: Trade log page
`/trade/log` SHALL list the signed-in user's trades newest first with partner username, date, cards given/received and outcome, in German, and SHALL only show trades the user took part in.

#### Scenario: Only own trades
- **WHEN** user C opens `/trade/log`
- **THEN** trades between A and B are not listed

#### Scenario: Immutable
- **WHEN** a log entry exists
- **THEN** no UI or service operation edits or deletes it
