## MODIFIED Requirements

### Requirement: Cards are rendered as a grouped table
The `/table` page SHALL render scanned cards in one table per group, each row showing a clickable thumbnail, card name (or file name if undetected) with the image file name, card number, an editable set selector, `AnalysisStatus` badge, `ReviewStatus` badge, rarity, a placeholder tags display derived client-side from rarity, confidence, and a details toggle.

#### Scenario: Viewing the table with scanned cards present
- **WHEN** a user opens `/table` and at least one scanned card exists
- **THEN** cards are rendered in grouped tables with a row per card, showing the columns Bild, Name, Nummer, Set, Status, Review-Status, Seltenheit, Tags, Confidence, and Details

#### Scenario: Tags are derived from rarity, not a stored field
- **WHEN** a row's tags display is rendered
- **THEN** its value is computed from that card's rarity rather than read from a separate stored `Tags` field, since no such field exists in the sidecar or catalog data yet
