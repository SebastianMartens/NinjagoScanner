## ADDED Requirements

### Requirement: The card overlay reflects the catalog rarity
The "Neue Karte" and "Dublette" card overlays SHALL take their rarity from the matched catalog card (`common`, `limited` or `legendary`), not from the photo's sidecar. A `limited` card SHALL show a "Limited" chip and the limited accent colour, a `legendary` card a "Legendär" chip and the legendary accent colour; a `common` card SHALL show no rarity chip and the default accent colour. The rank-up overlay SHALL be unaffected.

#### Scenario: New limited card
- **WHEN** a person uploads a photo that is matched to a new catalog card with rarity `limited`
- **THEN** the "Neue Karte" overlay shows a "Limited" chip and the limited accent colour

#### Scenario: New common card
- **WHEN** a person uploads a photo that is matched to a new catalog card with rarity `common`
- **THEN** the overlay shows no rarity chip and the default accent colour

#### Scenario: Review correction to a limited card
- **WHEN** a review correction makes a photo the sole owned copy of a catalog card with rarity `limited`
- **THEN** the overlay shows the "Limited" chip, whatever rarity the photo's previous match had
