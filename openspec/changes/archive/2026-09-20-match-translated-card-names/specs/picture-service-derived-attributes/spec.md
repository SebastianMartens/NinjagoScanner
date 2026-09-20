## ADDED Requirements

### Requirement: An English card name is derived for lookup in the English-only catalog
When the detected attributes include a card name, the derived attributes SHOULD include `card_name_en`: the English name of the card, being the official English name if it is known and otherwise a faithful translation of the detected name. A card name that is already English SHALL be repeated unchanged, and character names SHALL be kept as they are. When the detected attributes include no card name, `card_name_en` SHALL be omitted. `card_name_en` SHALL be a scalar string on the same terms as any other derived attribute; its absence SHALL NOT fail derivation.

#### Scenario: German card name
- **WHEN** the detected card name is "Feuer-Drache"
- **THEN** the derived attributes include `card_name_en` with the English name of that card (e.g. "Fire Dragon")

#### Scenario: English card name
- **WHEN** the detected card name is already English
- **THEN** `card_name_en` repeats that name

#### Scenario: No detected card name
- **WHEN** the detected attributes contain no card name
- **THEN** the derived attributes contain no `card_name_en`
