## Purpose

Defines the third stage of card analysis: deterministic, non-LLM matching of the derived attributes against every card in the catalog to determine which catalog card a photo shows - and therefore its series and card number - producing the sidecar's Judged section.

## ADDED Requirements

### Requirement: Catalog matching runs without an LLM call
Determining a photo's card (series, card number, and card name) from its derived attributes SHALL NOT require any call to the Gemini API or any other language model.

#### Scenario: Matching uses only catalog data and prior stage output
- **WHEN** catalog matching runs for a photo whose attribute-detection and derived-attributes stages both succeeded
- **THEN** the series/card-number/card-name resolution is computed entirely from the derived attributes and the catalog snapshot already loaded from `CatalogService`, with no additional outbound Gemini call

### Requirement: The best-scoring catalog card determines the series and card number
Stage 3 SHALL NOT resolve a series on its own. It SHALL score every card in the catalog, across all series, against the derived attributes only (`card_number`, `class`, `card_name`; the detected attributes and any series-name guess are not consulted), and the single highest-scoring card SHALL be the match: that card's series is the Judged series name and its card number is the Judged card number. A card SHALL only be a match when its score reaches a minimum, and when the highest score is shared by cards that are not the same series and card number, there is no match. The same series and card number listed under more than one category is one card.

#### Scenario: Card determines the series
- **WHEN** the derived card number is "1", which exists in several series, and the derived card name exactly matches the name of card 1 in "Serie 2" only
- **THEN** the Judged series name is "Serie 2" and the Judged card number is "1"

#### Scenario: Number alone identifies a card that exists in one series
- **WHEN** the derived card number exists in exactly one series of the catalog and nothing contradicts it
- **THEN** that card is the match

#### Scenario: A number shared by several series is not enough
- **WHEN** the derived card number exists in several series, and no name or class distinguishes them
- **THEN** there is no match

#### Scenario: A derived series-name guess is ignored
- **WHEN** the derived attributes contain a series-name guess
- **THEN** it has no influence on which card is matched

### Requirement: Card score combines card number, class and card name
A catalog card's score SHALL be the sum of: 50 points when the card number equals the derived card number; 20 points when the card's class equals the derived `class`; and up to 30 points for the card name, scaled by the name's similarity to the derived `card_name`. Card numbers SHALL be compared numerically ("7", "007" and 7.0 are the same number), and a derived card number of 0 SHALL be treated as absent. A derived `class` that is not one of the fixed class values, and a catalog card without a class, SHALL be treated as giving no class signal. A card SHALL be a match candidate only at 50 points or more.

#### Scenario: Card number, class and name all agree
- **WHEN** a card matches the derived card number, class and card name exactly
- **THEN** it scores the maximum of 100 points and outranks every card that misses any of the three

#### Scenario: Class with an exact name is enough without a number
- **WHEN** the derived card number is absent or matches nothing, and a card matches the derived class and card name exactly
- **THEN** that card is a match candidate

#### Scenario: Class with only a partial name is not enough
- **WHEN** a card matches the derived class and only partially matches the derived card name, and nothing else matches
- **THEN** the card is not a match candidate

### Requirement: Card names are compared by similarity, not only equality
The card-name component SHALL award the full 30 points only for a name that equals the derived card name after normalization (case, punctuation and surrounding whitespace ignored). A name that is merely similar - a different language, or a partially detected name such as a fragment of the real name - SHALL award a proportionally smaller amount, and a name below a minimum similarity SHALL award nothing, so an exact name always outranks a similar one.

#### Scenario: Partially detected name
- **WHEN** the derived card name is a fragment of a catalog card's name (e.g. "Jay Z" for "Jay ZX")
- **THEN** that card receives a reduced name score, lower than an exact name match would receive

#### Scenario: Unrelated name
- **WHEN** the derived card name is unrelated to a catalog card's name
- **THEN** the name contributes no points for that card

### Requirement: A card number with a different class is inconsistent
When the derived class and a catalog card's class are both known and differ, a card number equal to the derived one SHALL earn no points for that card, because a number that belongs to a card of another class means the number was misread or the class was misjudged. Such a card can still be matched through its name, but never through its number.

#### Scenario: Same number, different class
- **WHEN** the only catalog card with the derived card number has a different class than the derived class
- **THEN** there is no match

#### Scenario: Wrong-class number does not beat a right-class name
- **WHEN** a card with the derived number has a different class, and another card of the derived class matches the derived card name exactly
- **THEN** the card of the derived class is the match

### Requirement: Judged analysis status reflects detection, derivation, and matching outcomes
The sidecar's `Judged` section's analysis status SHALL be `failed` when attribute detection or derived-attribute computation failed for the photo, or when catalog matching finds no matching card. When a card is matched, the status SHALL be `ok` or `uncertain` (never `failed`); the specific split between `ok` and `uncertain` in that case is not fixed by this requirement (see design.md's Open Questions).

#### Scenario: Attribute detection failed
- **WHEN** the attribute-detection stage failed for a photo
- **THEN** the Judged analysis status is `failed`, and catalog matching is not attempted

#### Scenario: Derived-attribute computation failed
- **WHEN** attribute detection succeeded but derived-attribute computation failed
- **THEN** the Judged analysis status is `failed`, and catalog matching is not attempted

#### Scenario: No matching card
- **WHEN** attribute detection and derived-attribute computation both succeeded, but no catalog card is a match
- **THEN** the Judged analysis status is `failed`

#### Scenario: A card is matched
- **WHEN** catalog matching finds a matching card
- **THEN** the Judged analysis status is `ok` or `uncertain`, never `failed`

### Requirement: Verified series and card number survive re-analysis
When a photo that already has a sidecar is analyzed again and that sidecar's Review Status is `verified`, a human has already confirmed its series and card number, so analysis SHALL NOT judge them again. Attribute detection and derived-attribute computation SHALL still run and replace the `Detected` and `Derived` sections, but the `Judged` section's series name and card number SHALL keep their existing values instead of being resolved from the new attributes. The remaining Judged fields that catalog matching produces (card name, language) SHALL still be recomputed, with the card name taken from the catalog entry for the verified series and card number when one exists. A `verified` sidecar that has no series name or no card number has nothing to keep, and is analyzed like any other photo.

#### Scenario: Re-analysis of a verified photo keeps series and card number
- **WHEN** a photo whose sidecar has Review Status `verified`, series "Serie 1" and card number "1" is analyzed again, and the new attributes point to a different series and card number
- **THEN** detection and derivation run again and replace the `Detected` and `Derived` sections, and the Judged series name and card number remain "Serie 1" and "1"

#### Scenario: Verified photo is not failed for lack of a confident match
- **WHEN** a `verified` photo is analyzed again and the new attributes would not match any catalog card
- **THEN** the Judged analysis status is `ok`, with the verified series and card number unchanged

#### Scenario: Stage failure does not discard the verified series and card number
- **WHEN** a `verified` photo is analyzed again and attribute detection or derived-attribute computation fails
- **THEN** the Judged analysis status is `failed`, and the Judged series name and card number remain the verified values

#### Scenario: Photos that are not verified are judged again
- **WHEN** a photo whose Review Status is `unreviewed` or `incorrect` is analyzed again
- **THEN** the card is matched from the new attributes as for a first analysis

### Requirement: Unresolved matches preserve the raw guess rather than storing nothing
When no catalog card is matched, the Judged section's card number and card name SHALL still be populated with the derived attributes' raw values (if any), rather than being left empty, so a human reviewer has something to correct from. The Judged series name is left empty, since the series is only known through a matched card.

#### Scenario: Unresolved match keeps the raw guess
- **WHEN** no catalog card is matched but the derived attributes include a card number and card name
- **THEN** the Judged section's card number and card name are set to those raw values, its series name is empty, and the analysis status is `failed`
