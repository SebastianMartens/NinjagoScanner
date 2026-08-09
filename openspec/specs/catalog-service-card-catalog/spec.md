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
`ListAllCards` SHALL return every card known across all series' detail data, each with its series name, series sort order, category, card number, and card name.

#### Scenario: Listing all cards
- **WHEN** a client calls `ListAllCards`
- **THEN** the response contains one `CatalogCardEntry` for each unique combination of series name, category, card number, and card name found in the catalog data, with `sort_order` populated from that card's series

### Requirement: Duplicate card entries are collapsed
A card entry that is identical in series name and normalized card number to another entry SHALL appear only once in the response; a catalog card is uniquely identified by its series name and card number.

#### Scenario: Same card listed twice in source data
- **WHEN** the same card (same series and card number, allowing for card-number formatting differences such as `"1"` vs `"01"`) is present more than once in the underlying detail data
- **THEN** `ListAllCards` includes it exactly once

#### Scenario: Series and card number alone identify a card
- **WHEN** two `CatalogCardEntry` results are compared
- **THEN** they represent the same catalog card if and only if their series name and normalized card number match, regardless of category or card name

### Requirement: Cards are sorted deterministically
Returned cards SHALL be ordered by series `sort_order` ascending, then category, then card number, then card name, all case-insensitively (except `sort_order`, which is compared numerically). Card numbers SHALL sort purely numeric values first, then `LE`-prefixed numbers, then `XXL`-prefixed numbers, then any other format, each group ordered numerically or alphabetically within itself.

#### Scenario: Response ordering
- **WHEN** a client calls `ListAllCards`
- **THEN** the returned cards are ordered by series `sort_order` ascending, then category, then card number using the defined sort order, with card name used as the final tiebreaker

#### Scenario: Series sort order determines card ordering regardless of series name text
- **WHEN** two series have names that would sort differently alphabetically than by their assigned `sort_order` (e.g. "Serie 10" has a lower `sort_order` than "Serie 2")
- **THEN** the returned cards are grouped in `sort_order` order, not alphabetical series-name order

### Requirement: Incomplete card entries are excluded
A raw card entry that has no usable card number or no card name SHALL be excluded from the response.

#### Scenario: Entry missing a card number or name
- **WHEN** a card entry in the underlying detail data has a blank or missing card number, or a blank or missing card name
- **THEN** that entry does not appear in the `ListAllCards` response
