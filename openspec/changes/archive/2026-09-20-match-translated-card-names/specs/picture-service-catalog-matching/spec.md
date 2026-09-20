## MODIFIED Requirements

### Requirement: Card names are compared by similarity, not only equality
The card-name component SHALL award the full 30 points only for a name that equals a derived card name after normalization (case, punctuation and surrounding whitespace ignored). The derived card names are `card_name` and, when present, its English translation `card_name_en`; a catalog card's name SHALL be compared against each of them and the higher similarity SHALL count, because most catalog names exist in English only and a card in another language would otherwise never match. A name that is merely similar - a partially detected name such as a fragment of the real name - SHALL award a proportionally smaller amount, and a name below a minimum similarity SHALL award nothing, so an exact name always outranks a similar one. When no `card_name_en` is derived, only `card_name` SHALL be compared. Matching SHALL NOT call any language model.

#### Scenario: Partially detected name
- **WHEN** the derived card name is a fragment of a catalog card's name (e.g. "Jay Z" for "Jay ZX")
- **THEN** that card receives a reduced name score, lower than an exact name match would receive

#### Scenario: Unrelated name
- **WHEN** the derived card name is unrelated to a catalog card's name
- **THEN** the name contributes no points for that card

#### Scenario: Card in another language matched through its English name
- **WHEN** the derived card name is "Feuer-Drache", the derived `card_name_en` is "Fire Dragon", and the catalog only holds the English name "Fire Dragon" for a card of the derived class
- **THEN** that card receives the full name score and is the match

#### Scenario: No English name derived
- **WHEN** the derived card name is in another language than the catalog's and no `card_name_en` is derived
- **THEN** the card name is compared as before and, being dissimilar, contributes no points

#### Scenario: Catalog holds the original-language name
- **WHEN** a catalog card's name equals the derived `card_name` exactly, even though `card_name_en` differs from it
- **THEN** that card receives the full name score

#### Scenario: Translation does not override the class rule
- **WHEN** a card's number matches but its class differs from the derived class, and its name matches only through `card_name_en`
- **THEN** the card number still earns nothing, as for any class mismatch
