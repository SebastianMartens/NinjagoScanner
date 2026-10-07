## ADDED Requirements

### Requirement: Trade XP is stored bonus XP
Completed trades SHALL add 25 XP per participating collection to its stored bonus XP, which the XP total already includes.

#### Scenario: Rank reflects trade XP
- **WHEN** a collection completes a trade
- **THEN** its XP total is 25 higher on the next read and its rank updates if a threshold is crossed
