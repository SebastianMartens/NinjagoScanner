## MODIFIED Requirements

### Requirement: List all cards across all series
`ListAllCards` SHALL return every card known across all series' detail data, each with its series name, series sort order, category, card number, and card name.

#### Scenario: Listing all cards
- **WHEN** a client calls `ListAllCards`
- **THEN** the response contains one `CatalogCardEntry` for each unique combination of series name, category, card number, and card name found in the catalog data, with `sort_order` populated from that card's series

### Requirement: Cards are sorted deterministically
Returned cards SHALL be ordered by series `sort_order` ascending, then category, then card number, then card name, all case-insensitively (except `sort_order`, which is compared numerically). Card numbers SHALL sort purely numeric values first, then `LE`-prefixed numbers, then `XXL`-prefixed numbers, then any other format, each group ordered numerically or alphabetically within itself.

#### Scenario: Response ordering
- **WHEN** a client calls `ListAllCards`
- **THEN** the returned cards are ordered by series `sort_order` ascending, then category, then card number using the defined sort order, with card name used as the final tiebreaker

#### Scenario: Series sort order determines card ordering regardless of series name text
- **WHEN** two series have names that would sort differently alphabetically than by their assigned `sort_order` (e.g. "Serie 10" has a lower `sort_order` than "Serie 2")
- **THEN** the returned cards are grouped in `sort_order` order, not alphabetical series-name order
