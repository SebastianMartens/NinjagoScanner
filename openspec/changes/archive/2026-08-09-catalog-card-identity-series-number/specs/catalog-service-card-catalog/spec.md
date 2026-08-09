## MODIFIED Requirements

### Requirement: Duplicate card entries are collapsed
A card entry that is identical in series name and normalized card number to another entry SHALL appear only once in the response; a catalog card is uniquely identified by its series name and card number.

#### Scenario: Same card listed twice in source data
- **WHEN** the same card (same series and card number, allowing for card-number formatting differences such as `"1"` vs `"01"`) is present more than once in the underlying detail data
- **THEN** `ListAllCards` includes it exactly once

#### Scenario: Series and card number alone identify a card
- **WHEN** two `CatalogCardEntry` results are compared
- **THEN** they represent the same catalog card if and only if their series name and normalized card number match, regardless of category or card name
