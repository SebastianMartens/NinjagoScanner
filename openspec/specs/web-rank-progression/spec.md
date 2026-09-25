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
