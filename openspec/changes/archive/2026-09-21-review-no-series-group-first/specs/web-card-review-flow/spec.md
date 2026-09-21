## MODIFIED Requirements

### Requirement: Groups are ordered by known series order, then card number
Every photo whose `SetName`/`CardNumber` pair does not resolve, after normalization, to a catalog card - including a blank `SetName`, a blank `CardNumber`, an unrecognized series, or a card number not found within an otherwise recognized series - SHALL be merged into exactly one catch-all group, which SHALL be ordered before every matched group (when it has any photos). The matched groups, each corresponding to a matched catalog card, SHALL follow it, ordered by that card's series' catalog `SortOrder`, then by `CardNumber` within the series using the same card-number rule used everywhere else in the application: purely numeric card numbers first ordered by value, then alphabetic-prefix-plus-number card numbers ordered by prefix alphabetically and then by numeric suffix, then anything else ordered alphabetically by raw text.

#### Scenario: Groups follow catalog series order
- **WHEN** the review page lists matched groups
- **THEN** they appear ordered by the series' catalog `SortOrder`, and by `CardNumber` within the same series

#### Scenario: Unrecognized and blank series are combined into one trailing group
- **WHEN** photos have a `SetName` that does not resolve to any known catalog series, or have no `SetName` at all
- **THEN** all such photos appear together in a single group that is ordered before every matched group

#### Scenario: Numeric and alphanumeric card numbers within the same series order correctly
- **WHEN** a series has matched groups for both purely numeric card numbers (e.g. `2`, `10`) and alphanumeric card numbers (e.g. `LE1`, `XXL1`)
- **THEN** groups for numeric card numbers appear first, ordered by value, followed by groups for alphanumeric card numbers ordered by prefix alphabetically and then by numeric suffix

#### Scenario: A recognized series with an unrecognized card number falls into the catch-all group
- **WHEN** a photo's `SetName` matches a known catalog series but its `CardNumber` does not match any card within that series
- **THEN** that photo is placed in the catch-all group rather than forming its own group

#### Scenario: No catch-all group when every photo resolves
- **WHEN** every photo's `SetName`/`CardNumber` resolves to a catalog card
- **THEN** no catch-all group is shown, and the first group is the matched group with the lowest series `SortOrder` and card number
