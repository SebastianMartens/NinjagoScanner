## ADDED Requirements

### Requirement: Attribute detection reports visual hints of rarity
The attribute-detection request SHALL ask the model for one additional attribute, `rarity_hint`, describing only what is visually observable on the photo that may indicate a card's rarity (for example a limited-edition marking such as an "LE" card number prefix, or a special finish). The attribute SHALL NOT be an interpretation: stage 1 does not decide whether the card is common or limited. When the photo shows no such hint, the value SHALL be empty.

#### Scenario: Photo shows a limited-edition marking
- **WHEN** the photo shows a card number with an "LE" prefix
- **THEN** the detected attributes contain a non-empty `rarity_hint` that mentions the observed marking

#### Scenario: Photo shows no rarity hint
- **WHEN** the photo shows an ordinary card without special markings or finish
- **THEN** the detected attributes contain an empty `rarity_hint`

#### Scenario: Stage 1 does not classify rarity
- **WHEN** attribute detection succeeds
- **THEN** the detected attributes contain no `rarity` key
