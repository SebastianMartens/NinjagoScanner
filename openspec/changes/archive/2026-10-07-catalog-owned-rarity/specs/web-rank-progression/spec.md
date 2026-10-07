## ADDED Requirements

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
