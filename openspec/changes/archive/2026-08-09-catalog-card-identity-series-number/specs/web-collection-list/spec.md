## MODIFIED Requirements

### Requirement: The overview covers every catalog card, not just owned ones
The `/collection` page SHALL list every card returned by CatalogService's card catalog, each annotated with the number of owned photo copies (`OwnedCopies`) determined by matching the card's series and card number, normalized, against scanned photo sidecars' set name and card number.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so this match is always unambiguous: a photo's `OwnedCopies` contribution is attributed to exactly one catalog card.

#### Scenario: A catalog card with no matching photo
- **WHEN** a catalog card has no scanned photo whose set name and card number match it after normalization
- **THEN** the card still appears in the overview with `OwnedCopies` equal to zero and is treated as not owned

#### Scenario: A catalog card with multiple matching photos
- **WHEN** more than one scanned photo's set name and card number match the same catalog card after normalization
- **THEN** the card's `OwnedCopies` reflects the count of matching photos and it is treated as a duplicate
