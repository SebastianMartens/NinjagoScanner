## MODIFIED Requirements

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
