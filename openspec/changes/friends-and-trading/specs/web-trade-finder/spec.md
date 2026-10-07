## Purpose

Makes it easy to find trade partners and concrete swaps by matching one user's duplicate cards against a friend's missing cards and vice versa, balanced by rarity.

## ADDED Requirements

### Requirement: Tradable and wanted cards
For a user, a card is **tradable** when they own more than one copy of that catalog card (surplus = copies - 1 copies may be offered), and **wanted** when the catalog contains it and they own zero copies. Unmapped photos SHALL NOT be tradable.

#### Scenario: Duplicate is tradable
- **WHEN** a user owns 3 photos mapped to one catalog card
- **THEN** up to 2 copies of it are tradable

#### Scenario: Single copy not tradable
- **WHEN** a user owns exactly one copy
- **THEN** it is not offered in any trade suggestion

#### Scenario: Unmapped photo not tradable
- **WHEN** a photo has no catalog match
- **THEN** it is not tradable

### Requirement: Partner ranking
`/trade` SHALL rank the user's visible friends by the number of cards that can be exchanged in both directions (my tradable cards the friend wants, and the friend's tradable cards I want), highest first, and show for each friend the counts in both directions. Friends who have set visibility to `Nur ich` SHALL be excluded and shown as unavailable.

#### Scenario: Ranking order
- **WHEN** friend X can swap 5 cards with me and friend Y can swap 2
- **THEN** X is listed before Y

#### Scenario: No overlap
- **WHEN** a friend has nothing I want and wants nothing I offer
- **THEN** they are listed last with zero counts

### Requirement: Rarity-balanced suggestion
For a chosen friend the finder SHALL propose an exchange where cards given and received have equal count and, where possible, equal rarity tiers (`common`, `limited`, `legendary`) pairwise. When no equal-tier pairing exists, it SHALL fall back to a value-balanced pairing using rarity weights (common 1, limited 3, legendary 9) and SHALL show the weight difference. Pairs whose difference exceeds one tier SHALL be flagged `Unausgewogen`.

#### Scenario: Equal-tier pairing
- **WHEN** I offer two common duplicates the friend wants and the friend offers two common cards I want
- **THEN** the suggestion pairs them 2-for-2 as balanced

#### Scenario: Limited for common is flagged
- **WHEN** only a limited card of mine matches a friend's common card
- **THEN** that pairing is shown flagged `Unausgewogen`

#### Scenario: Suggestion respects surplus
- **WHEN** I own two copies of a card
- **THEN** no suggestion offers more than one copy of it

### Requirement: Manual adjustment
The user SHALL be able to deselect individual cards or swap in other tradable/wanted cards before proposing, with the balance indicator updating accordingly.

#### Scenario: Deselect
- **WHEN** the user removes a card from a suggestion
- **THEN** the balance indicator is recomputed
