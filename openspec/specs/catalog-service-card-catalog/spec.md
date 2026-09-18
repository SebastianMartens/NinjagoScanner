# catalog-service-card-catalog Specification

## Purpose

Provides a flattened, deduplicated, consistently ordered view of every individual card across all series, for downstream cataloging and scanning workflows.

<!-- RESOLVED: series_name + card_number is confirmed unique across the
catalog data (verified during the catalog-card-identity-series-number
change), so a card's identity key is (series_name, card_number). Category
remains a real, displayed/filterable attribute but is no longer needed to
disambiguate two cards; see GLOSSARY.md's Card / Card Number / Category
entries. Card name stays excluded from identity (translations give it
multiple values per card). -->

## Requirements

### Requirement: List all cards across all series
`ListAllCards` SHALL return every card known across all series' detail data, each with its series name, series sort order, category, class, card number, and card name.

#### Scenario: Listing all cards
- **WHEN** a client calls `ListAllCards`
- **THEN** the response contains one `CatalogCardEntry` for each unique combination of series name, category, card number, and card name found in the catalog data, with `sort_order` populated from that card's series and `class` populated from that card's category

### Requirement: Every category maps to exactly one class
Each card's `category` SHALL determine its `class` from a fixed, catalog-wide set of classes (`character`, `action`, `vehicle`, `puzzle-piece`, `trap`, `limited edition`, `art`). A given category name SHALL map to the same class everywhere it appears across the catalog's series data, even though the mapping is declared separately per series file.

#### Scenario: Same category name in multiple series files
- **WHEN** a category name (e.g. `Heroes`) appears in more than one series file with a declared class
- **THEN** its declared class is identical in every file it appears in

#### Scenario: Category present in the catalog data has no class
- **WHEN** a series file's data is loaded and a category entry has no `Class` declared
- **THEN** loading the catalog fails fast rather than silently returning cards with an empty or absent class

### Requirement: Duplicate card entries are collapsed
A card entry that is identical in series name and normalized card number to another entry SHALL appear only once in the response; a catalog card is uniquely identified by its series name and card number.

#### Scenario: Same card listed twice in source data
- **WHEN** the same card (same series and card number, allowing for card-number formatting differences such as `"1"` vs `"01"`) is present more than once in the underlying detail data
- **THEN** `ListAllCards` includes it exactly once

#### Scenario: Series and card number alone identify a card
- **WHEN** two `CatalogCardEntry` results are compared
- **THEN** they represent the same catalog card if and only if their series name and normalized card number match, regardless of category or card name

### Requirement: Cards are sorted deterministically
Returned cards SHALL be ordered by series `sort_order` ascending, then card number, then card name, all case-insensitively (except `sort_order`, which is compared numerically). Category is not part of the ordering; it remains a real, filterable, displayed field. Card numbers SHALL sort as follows: purely numeric card numbers first, ordered by their numeric value; then any card number consisting of an alphabetic prefix followed by a number (e.g. `LE4`, `XXL1`, or any other prefix), ordered by that prefix alphabetically and then by its numeric suffix; then any remaining card number that matches neither pattern, ordered alphabetically by its raw text.

#### Scenario: Response ordering
- **WHEN** a client calls `ListAllCards`
- **THEN** the returned cards are ordered by series `sort_order` ascending, then card number using the defined sort order, with card name used as the final tiebreaker

#### Scenario: Series sort order determines card ordering regardless of series name text
- **WHEN** two series have names that would sort differently alphabetically than by their assigned `sort_order` (e.g. "Serie 10" has a lower `sort_order` than "Serie 2")
- **THEN** the returned cards are grouped in `sort_order` order, not alphabetical series-name order

#### Scenario: Numeric card numbers sort before alphanumeric ones
- **WHEN** a series contains both purely numeric card numbers (e.g. `2`, `10`) and alphanumeric card numbers (e.g. `LE1`, `XXL1`)
- **THEN** all numeric card numbers appear first, ordered by value (`2` before `10`), followed by all alphanumeric card numbers

#### Scenario: Alphanumeric card numbers sort by prefix alphabetically, regardless of which prefixes appear
- **WHEN** a series contains alphanumeric card numbers with more than two distinct prefixes (e.g. `LE1`, `LE3`, `OTHER1`, `XXL1`, `XXL2`)
- **THEN** they are ordered by prefix alphabetically (`LE` before `OTHER` before `XXL`), and within the same prefix by their numeric suffix ascending

#### Scenario: A category that starts later in the card-number range does not reorder earlier cards
- **WHEN** a series has a category whose lowest card number (e.g. `101`) is higher than another category's lowest card number (e.g. `1`), and the two category names would sort in the opposite order alphabetically (e.g. "Action Cards" before "Heroes")
- **THEN** the response is still ordered by card number, so the card numbered `1` appears before the card numbered `101` regardless of which category name sorts first alphabetically

### Requirement: Incomplete card entries are excluded
A raw card entry that has no usable card number, or that has no card name in any known language, SHALL be excluded from the response.

#### Scenario: Entry missing a card number or name
- **WHEN** a card entry in the underlying detail data has a blank or missing card number, or has no value for German or English (the only languages currently populated) in its name map
- **THEN** that entry does not appear in the `ListAllCards` response

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
