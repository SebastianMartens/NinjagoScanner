## MODIFIED Requirements

### Requirement: Photos are grouped by series and card number
The review page SHALL group every scanned photo by the pair of its own sidecar `SetName` and `CardNumber`, independent of the catalog, so a group exists if and only if at least one photo currently carries that `SetName`/`CardNumber` pair.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so a group's `SetName`/`CardNumber` pair corresponds to exactly one catalog card whenever it matches one.

#### Scenario: Photos sharing a series and card number are grouped together
- **WHEN** two or more photos have the same `SetName` and `CardNumber` in their sidecar
- **THEN** they appear together in the same group on the review page

#### Scenario: A catalog card with no photos never appears
- **WHEN** a catalog card has no photo whose sidecar `SetName`/`CardNumber` matches it
- **THEN** no group for that card is shown on the review page
