## REMOVED Requirements

### Requirement: Exact series name match wins
**Reason**: Stage 3 no longer resolves a series from a series-name guess. Stage 1/2 do not reliably produce one, so the series is now taken from the best-scoring catalog card (see `picture-service-catalog-matching`).
**Migration**: None - no other code resolves a series from a name guess.

### Requirement: Evidence-based matching when no exact name match exists
**Reason**: Series are no longer scored from evidence text (symbol/logo hint, year, known card names); the series follows from the matched card.
**Migration**: None.

### Requirement: A scoring tie yields no match
**Reason**: The tie rule now applies to catalog cards rather than series, and is part of `picture-service-catalog-matching`'s "The best-scoring catalog card determines the series and card number".
**Migration**: None.

### Requirement: Empty catalog falls back to the model's raw guess
**Reason**: No series is resolved from a guess any more; with an empty catalog no card matches and the raw card number and card name are kept (see `picture-service-catalog-matching`'s "Unresolved matches preserve the raw guess").
**Migration**: None.
