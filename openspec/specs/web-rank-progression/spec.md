# web-rank-progression Specification

## Purpose

Defines how a collection earns experience points (XP) and how its total XP maps onto the rank ladder shown across the Web app.

## Requirements

### Requirement: XP is earned from new cards, confirmed reviews, and achievements, but not from duplicate copies
A collection's XP total SHALL be derived from its current state as the sum of:
- 50 XP for each distinct catalog card with at least one owned copy,
- 10 XP for each photo whose Review Status is `verified`,
- each currently unlocked achievement's own XP value,
- any stored bonus XP.

Owned copies beyond the first for the same catalog card (duplicate copies) SHALL NOT contribute any XP. Because XP is derived, a collection's XP and rank SHALL reflect this rule on the next read, including for duplicate copies that already existed before this rule applied.

#### Scenario: A duplicate copy adds no XP
- **WHEN** a collection owns one copy of a catalog card, and a second photo matching that same card is added
- **THEN** the collection's XP total is unchanged by the second photo, apart from any achievement it unlocks

#### Scenario: A new card still adds XP
- **WHEN** a photo is added that matches a catalog card the collection didn't own before
- **THEN** the collection's XP total increases by 50, plus the XP of any achievement it unlocks

#### Scenario: Existing duplicates no longer count toward XP
- **WHEN** a collection holds two photos of one catalog card and one photo of another, with no verified reviews and no achievements beyond `first-scan`
- **THEN** its XP total is `first-scan`'s XP plus 2 × 50, with nothing for the duplicate copy

#### Scenario: The duplicate-count achievement still awards its own XP
- **WHEN** a collection reaches 25 duplicate copies and the `dupes-25` achievement unlocks
- **THEN** the collection's XP total includes that achievement's XP, even though the duplicate copies themselves contribute none

### Requirement: Rarity achievements follow the catalog rarity
Achievements in the "Seltenheit" category SHALL be evaluated from the catalog rarity of the catalog cards the collection owns (a card is owned when at least one photo matches it), never from a photo's sidecar. The `limited-hunter` achievement ("Limit-Jäger") SHALL count distinct owned catalog cards with rarity `limited` and unlock at 5. The `first-legendary` achievement ("Legendenbrecher") SHALL unlock when the collection owns at least one catalog card with rarity `legendary`; it stays locked while no card in the catalog is legendary.

#### Scenario: Limited cards count once each
- **WHEN** a collection owns three photos of one `limited` catalog card and one photo each of four other `limited` catalog cards
- **THEN** `limited-hunter` progress is 5 of 5 and it is unlocked

#### Scenario: Common cards do not count
- **WHEN** a collection owns many `common` catalog cards and no `limited` one
- **THEN** `limited-hunter` progress is 0

#### Scenario: Photo sidecar rarity is irrelevant
- **WHEN** a photo matched to a `common` catalog card has a legacy rarity value of `limited edition` in its sidecar
- **THEN** it does not count toward `limited-hunter`

#### Scenario: First legendary card unlocks the achievement
- **WHEN** a collection owns a photo matched to a catalog card with rarity `legendary`
- **THEN** `first-legendary` is unlocked

#### Scenario: Locked while no card is legendary
- **WHEN** no catalog card has rarity `legendary`
- **THEN** `first-legendary` is shown as locked with progress 0 of 1

### Requirement: Trade XP is stored bonus XP
Completed trades SHALL add 25 XP per participating collection to its stored bonus XP, which the XP total already includes.

#### Scenario: Rank reflects trade XP
- **WHEN** a collection completes a trade
- **THEN** its XP total is 25 higher on the next read and its rank updates if a threshold is crossed
