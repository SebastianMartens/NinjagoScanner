## RENAMED Requirements

- FROM: `### Requirement: Card score combines card number, class and card name`
- TO: `### Requirement: Card score combines card number, class, rarity and card name`

## MODIFIED Requirements

### Requirement: The best-scoring catalog card determines the series and card number
Stage 3 SHALL NOT resolve a series on its own. It SHALL score every card in the catalog, across all series, against the derived attributes only (`card_number`, `class`, `rarity`, `card_name`; the detected attributes and any series-name guess are not consulted), and the single highest-scoring card SHALL be the match: that card's series is the Judged series name and its card number is the Judged card number. A card SHALL only be a match when its score reaches a minimum, and when the highest score is shared by cards that are not the same series and card number, there is no match. The same series and card number listed under more than one category is one card.

#### Scenario: Card determines the series
- **WHEN** the derived card number is "1", which exists in several series, and the derived card name exactly matches the name of card 1 in "Serie 2" only
- **THEN** the Judged series name is "Serie 2" and the Judged card number is "1"

#### Scenario: Number alone identifies a card that exists in one series
- **WHEN** the derived card number exists in exactly one series of the catalog and nothing contradicts it
- **THEN** that card is the match

#### Scenario: A number shared by several series is not enough
- **WHEN** the derived card number exists in several series, and no name, class or rarity distinguishes them
- **THEN** there is no match

#### Scenario: A derived series-name guess is ignored
- **WHEN** the derived attributes contain a series-name guess
- **THEN** it has no influence on which card is matched

### Requirement: Card score combines card number, class, rarity and card name
A catalog card's score SHALL be the sum of: 50 points when the card number equals the derived card number; 20 points when the card's class equals the derived `class`; 10 points when the card's rarity equals the derived `rarity`; and up to 30 points for the card name, scaled by the name's similarity to the derived `card_name`. Card numbers SHALL be compared numerically ("7", "007" and 7.0 are the same number), and a derived card number of 0 SHALL be treated as absent. A derived `class` that is not one of the fixed class values, and a catalog card without a class, SHALL be treated as giving no class signal. A derived `rarity` that is not one of the fixed rarity values, and a catalog card without a rarity, SHALL be treated as giving no rarity signal. A differing rarity SHALL NOT subtract points or invalidate the card number. A card SHALL be a match candidate only at 50 points or more, counting only the points of number, class and name; rarity points alone or as the deciding margin to reach 50 SHALL NOT make a card a match candidate.

#### Scenario: Card number, class and name all agree
- **WHEN** a card matches the derived card number, class, rarity and card name exactly
- **THEN** it scores the maximum of 110 points and outranks every card that misses any of the four

#### Scenario: Class with an exact name is enough without a number
- **WHEN** the derived card number is absent or matches nothing, and a card matches the derived class and card name exactly
- **THEN** that card is a match candidate

#### Scenario: Class with only a partial name is not enough
- **WHEN** a card matches the derived class and only partially matches the derived card name, and nothing else matches
- **THEN** the card is not a match candidate, even if its rarity also matches

#### Scenario: Rarity breaks a tie between otherwise equal cards
- **WHEN** two catalog cards score equally on number, class and name, and only one of them has the derived rarity
- **THEN** the card with the derived rarity scores 10 points higher and is the match

#### Scenario: Rarity alone is not a match
- **WHEN** no card matches the derived card number, class or card name, but many cards have the derived rarity
- **THEN** there is no match

#### Scenario: A rarity mismatch does not invalidate the number
- **WHEN** the card with the derived card number has a different rarity than the derived rarity, and class and name agree
- **THEN** the card still earns its number points

## ADDED Requirements

### Requirement: Catalog cards carry class and rarity from CatalogService
The catalog snapshot used for matching SHALL carry each card's class and rarity as returned by `ListAllCards`. A card whose class or rarity is empty in the response SHALL be treated as having none, giving no signal for that attribute.

#### Scenario: Class and rarity are taken from the catalog response
- **WHEN** the catalog snapshot is loaded and `ListAllCards` returns a card with class `limited edition` and rarity `limited`
- **THEN** the snapshot's card has class `limited edition` and rarity `limited`

#### Scenario: Class scoring is effective
- **WHEN** the derived class equals a catalog card's class
- **THEN** that card earns the class points
