## REMOVED Requirements

### Requirement: Cards are rendered as tiles with detected details
**Reason**: The tile gallery on "/" is replaced by the new Overview page (`web-overview`) plus the dedicated review workflow (`web-card-review-flow`); browsing every photo as a tile is superseded by `/table`.
**Migration**: Use `/table` to browse all scanned photos, and `/review` to validate/fix a card's photos.

### Requirement: Cards can be grouped
**Reason**: Grouping-while-browsing is covered by `/table`; the new review page groups by series/card-number for a different purpose (validation, not browsing).
**Migration**: Use `/table`'s grouping controls.

### Requirement: Cards can be filtered
**Reason**: Filtering-while-browsing is covered by `/table`.
**Migration**: Use `/table`'s search/status/set/rarity filters.

### Requirement: Empty and no-match states are distinguished
**Reason**: This applied to the removed tile gallery's filter UI, which no longer exists.
**Migration**: Equivalent empty/no-match states are provided by `/table`.

### Requirement: A manual Gemini scan can be triggered
**Reason**: The scan trigger moves to the new Overview page ("/").
**Migration**: Trigger scans from the Overview page; see `web-overview`.
