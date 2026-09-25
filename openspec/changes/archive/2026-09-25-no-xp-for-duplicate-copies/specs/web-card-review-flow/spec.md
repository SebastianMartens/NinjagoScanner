## ADDED Requirements

### Requirement: Correcting a photo to an already-owned card raises no celebration
When a correction on the review page (series, card number, or a re-analysis result) changes a photo's matched catalog card to one the collection already owns another copy of, the review page SHALL NOT show a card overlay, a rank-up overlay, or any XP notification. The only feedback it SHALL show for such a correction is a toast for each achievement the correction newly unlocked. The correction itself SHALL be saved and reflected on the page exactly as for any other correction.

#### Scenario: Card number correction to a duplicate
- **WHEN** a person corrects a photo's card number on the review page to the number of a card in the same series that the collection already owns
- **THEN** the photo moves to that card's group and no overlay or notification is shown

#### Scenario: Series correction to a duplicate
- **WHEN** a person reassigns a photo to a different series on the review page, and the photo then matches a card the collection already owns
- **THEN** the photo moves to that card's group and no overlay or notification is shown

#### Scenario: Re-analysis resolves to a duplicate
- **WHEN** a photo's re-analysis on the review page resolves it to a card the collection already owns another copy of
- **THEN** the tile updates to the new result and no overlay or notification is shown
