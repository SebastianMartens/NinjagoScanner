## ADDED Requirements

### Requirement: Card tiles show the catalog rarity as a tag
Each card tile SHALL show a tag for the card's rarity as defined by the catalog, independent of whether a photo is matched to the card. A card with rarity `limited` SHALL show a "Limited" tag, a card with rarity `legendary` a "Legendär" tag, and a card with rarity `common` no rarity tag. The tag SHALL NOT depend on any sidecar value.

#### Scenario: Limited card shows its tag
- **WHEN** the gallery shows a catalog card whose rarity is `limited`
- **THEN** its tile shows a "Limited" tag, whether the tile shows a photo or a placeholder

#### Scenario: Common card shows no tag
- **WHEN** the gallery shows a catalog card whose rarity is `common`
- **THEN** its tile shows no rarity tag

#### Scenario: Photo content does not influence the tag
- **WHEN** a photo is matched to a `common` catalog card but its sidecar holds a legacy rarity value
- **THEN** the tile shows no rarity tag
