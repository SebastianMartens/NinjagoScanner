## MODIFIED Requirements

### Requirement: List all cards across all series
`ListAllCards` SHALL return every card known across all series' detail data, each with its series name, series sort order, category, class, rarity, card number, and card name.

#### Scenario: Listing all cards
- **WHEN** a client calls `ListAllCards`
- **THEN** the response contains one `CatalogCardEntry` for each unique combination of series name, category, card number, and card name found in the catalog data, with `sort_order` populated from that card's series, `class` populated from that card's category, and `rarity` populated from the card's declared or inherited rarity

## ADDED Requirements

### Requirement: Every card has a rarity declared in the catalog data
Each card SHALL have a rarity from a fixed, catalog-wide set: `common`, `limited` and `legendary`. The rarity is declared in the series data, like the class: a category declares it once and its cards inherit it, and an individual card entry MAY declare its own rarity, which then overrides the category's. A card with no declared or inherited rarity, or with a value outside the set, SHALL fail catalog loading rather than be returned with an empty or invalid rarity. The rarity is independent of the class: it was seeded once from the class (`limited edition` → `limited`, every other class → `common`), but later changes to either do not affect the other.

#### Scenario: Category rarity is inherited
- **WHEN** a category declares rarity `limited`
- **THEN** every card in it has rarity `limited`

#### Scenario: A card overrides its category
- **WHEN** a category declares rarity `common` and one of its card entries declares rarity `legendary`
- **THEN** that card has rarity `legendary` and the other cards in the category have `common`

#### Scenario: Current data was seeded from the class
- **WHEN** the shipped series data is loaded
- **THEN** every card of class `limited edition` has rarity `limited` and every other card has rarity `common`

#### Scenario: Missing rarity fails loading
- **WHEN** a card has neither a declared nor an inherited rarity
- **THEN** loading the catalog fails fast

#### Scenario: Unknown rarity fails loading
- **WHEN** a category or card declares a rarity outside `common`, `limited`, `legendary`
- **THEN** loading the catalog fails fast

#### Scenario: Rarity is never empty
- **WHEN** a client calls `ListAllCards`
- **THEN** every returned `CatalogCardEntry` has a rarity from the fixed set
