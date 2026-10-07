## MODIFIED Requirements

### Requirement: Additional photo details are collapsed by default
Each photo tile SHALL hide its remaining sidecar fields (confidence, reasoning summary, detected text, error message, scanned-at timestamp) and the matched catalog card's rarity ("Seltenheit") behind an on-demand control, collapsed by default. The rarity SHALL come from the catalog card the photo's group resolves to, not from the photo's sidecar; a photo that matches no catalog card SHALL show no rarity value.

#### Scenario: Expanding a photo's details
- **WHEN** a user activates the details control on a photo tile
- **THEN** that photo's remaining sidecar fields and the catalog card's rarity become visible, without affecting other photo tiles

#### Scenario: Rarity follows the photo's matched card
- **WHEN** a photo's card number is corrected so it resolves to a catalog card with a different rarity and its details are expanded
- **THEN** the details show the rarity of the newly matched catalog card

#### Scenario: Unmatched photo shows no rarity
- **WHEN** a photo that matches no catalog card has its details expanded
- **THEN** no rarity value is shown for it

### Requirement: The review page reflects a finished re-analysis without a reload
When a photo's re-analysis succeeds, the review page SHALL update that photo's tile to show the newly written series name, card name, card number, language, and `AnalysisStatus`, updating that photo's card number and language controls to the new values. If the photo's details section is expanded, or is expanded later, it SHALL show the new analysis result rather than a previously loaded one. If the new `SetName`/`CardNumber` resolve to a different catalog card, the photo SHALL appear under the group for that card and the page SHALL stay on the group it was showing when that group still exists, otherwise on the nearest remaining group, using the same navigation rule as any other photo change.

#### Scenario: Re-analysis corrects a misread card number
- **WHEN** a re-analysis of an `unreviewed` photo completes with a different card number than before
- **THEN** the tile shows the new card number in its label and card number control, and the photo appears under the group matching its new series and card number

#### Scenario: Expanded details show the new result
- **WHEN** a photo's details are expanded and its re-analysis completes
- **THEN** the details section shows the new scanned-at timestamp, error message, and attributes data, not the values from before the re-analysis

#### Scenario: A re-analysis that leaves the match unchanged
- **WHEN** a re-analysis completes and the photo still resolves to the same catalog card
- **THEN** the page stays on the same group and the photo stays in it
