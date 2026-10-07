## ADDED Requirements

### Requirement: Derived rarity is one of the catalog's fixed rarity values
The derived-attributes request SHALL ask the model to derive a `rarity` from the detected attributes (in particular `rarity_hint` and `card_number`, together with the derived `class`). When the derived attributes include a `rarity` value, it SHALL be one of the catalog's fixed set of rarities (`common`, `limited`, `legendary`). The request SHALL only offer `common` and `limited` as values to derive, since a legendary card cannot be recognised from a photo. A value outside this set SHALL be treated as if no rarity was derived, not passed through as-is.

#### Scenario: Limited edition card
- **WHEN** the detected attributes include a `rarity_hint` describing an "LE" card number prefix
- **THEN** the derived `rarity` is `limited`

#### Scenario: Recognized rarity value
- **WHEN** the derived attributes include a `rarity` value matching one of the fixed set
- **THEN** that value is kept as the derived rarity

#### Scenario: Unrecognized rarity value
- **WHEN** the derived attributes include a `rarity` value that does not match any value in the fixed set (for example `rare`)
- **THEN** the derived rarity is treated as absent rather than storing the unrecognized value
