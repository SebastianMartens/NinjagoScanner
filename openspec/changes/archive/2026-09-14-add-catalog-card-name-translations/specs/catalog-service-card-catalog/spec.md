## MODIFIED Requirements

### Requirement: Incomplete card entries are excluded
A raw card entry that has no usable card number, or that has no card name in any known language, SHALL be excluded from the response.

#### Scenario: Entry missing a card number or name
- **WHEN** a card entry in the underlying detail data has a blank or missing card number, or has no value for German or English (the only languages currently populated) in its name map
- **THEN** that entry does not appear in the `ListAllCards` response

## ADDED Requirements

### Requirement: Card name resolves by language preference
A card's underlying name is stored as a set of values by language rather than a single string. The system SHALL resolve a card's `card_name` by preferring the German value when present, otherwise falling back to the English value.

#### Scenario: German name available
- **WHEN** a card entry's name map has a German value
- **THEN** `card_name` is the German value, regardless of whether an English value is also present

#### Scenario: Only English name available
- **WHEN** a card entry's name map has an English value but no German value
- **THEN** `card_name` is the English value

### Requirement: Duplicate detection ignores which language a name came from
Two entries for the same series and card number SHALL be treated as the same card for deduplication purposes even if their underlying name maps have values in different languages, since `card_name` is always the single resolved value.

#### Scenario: Same card, names sourced from different languages across duplicate entries
- **WHEN** the same series-and-card-number entry appears more than once in the underlying detail data with name maps that differ only in which language is populated
- **THEN** `ListAllCards` includes it exactly once, using the resolved `card_name` from the entry that is kept
