## Purpose

Provides a flattened, deduplicated, consistently ordered view of every individual card across all series, for downstream cataloging and scanning workflows.

<!-- TODO (not yet resolved, to be addressed later): Cards should be uniquely
identified by series name + card number (both strings) rather than by
series + category + name. Category is only an attribute of a card, not part
of its identity. Card name will later have multiple values per card due to
translations, so identity/dedup logic and requirements below need to be
revisited to key on (series_name, card_number) instead. -->

## ADDED Requirements

### Requirement: List all cards across all series
`ListAllCards` SHALL return every card known across all series' detail data, each with its series name, category, card number, and card name.

#### Scenario: Listing all cards
- **WHEN** a client calls `ListAllCards`
- **THEN** the response contains one `CatalogCardEntry` for each unique combination of series name, category, card number, and card name found in the catalog data

### Requirement: Duplicate card entries are collapsed
A card entry that is identical in series name, category, normalized card number, and card name to another entry SHALL appear only once in the response.

#### Scenario: Same card listed twice in source data
- **WHEN** the same card (same series, category, card number, and name) is present more than once in the underlying detail data
- **THEN** `ListAllCards` includes it exactly once

### Requirement: Cards are sorted deterministically
Returned cards SHALL be ordered by series name, then category, then card number, then card name, all case-insensitively. Card numbers SHALL sort purely numeric values first, then `LE`-prefixed numbers, then `XXL`-prefixed numbers, then any other format, each group ordered numerically or alphabetically within itself.

#### Scenario: Response ordering
- **WHEN** a client calls `ListAllCards`
- **THEN** the returned cards are ordered by series name, then category, then card number using the defined sort order, with card name used as the final tiebreaker

### Requirement: Incomplete card entries are excluded
A raw card entry that has no usable card number or no card name SHALL be excluded from the response.

#### Scenario: Entry missing a card number or name
- **WHEN** a card entry in the underlying detail data has a blank or missing card number, or a blank or missing card name
- **THEN** that entry does not appear in the `ListAllCards` response
