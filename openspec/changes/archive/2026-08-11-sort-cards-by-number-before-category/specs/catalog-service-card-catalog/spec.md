## MODIFIED Requirements

### Requirement: Cards are sorted deterministically
Returned cards SHALL be ordered by series `sort_order` ascending, then card number, then card name, all case-insensitively (except `sort_order`, which is compared numerically). Category is not part of the ordering; it remains a real, filterable, displayed field. Card numbers SHALL sort as follows: purely numeric card numbers first, ordered by their numeric value; then any card number consisting of an alphabetic prefix followed by a number (e.g. `LE4`, `XXL1`, or any other prefix), ordered by that prefix alphabetically and then by its numeric suffix; then any remaining card number that matches neither pattern, ordered alphabetically by its raw text.

#### Scenario: Response ordering
- **WHEN** a client calls `ListAllCards`
- **THEN** the returned cards are ordered by series `sort_order` ascending, then card number using the defined sort order, with card name used as the final tiebreaker

#### Scenario: Series sort order determines card ordering regardless of series name text
- **WHEN** two series have names that would sort differently alphabetically than by their assigned `sort_order` (e.g. "Serie 10" has a lower `sort_order` than "Serie 2")
- **THEN** the returned cards are grouped in `sort_order` order, not alphabetical series-name order

#### Scenario: Numeric card numbers sort before alphanumeric ones
- **WHEN** a series contains both purely numeric card numbers (e.g. `2`, `10`) and alphanumeric card numbers (e.g. `LE1`, `XXL1`)
- **THEN** all numeric card numbers appear first, ordered by value (`2` before `10`), followed by all alphanumeric card numbers

#### Scenario: Alphanumeric card numbers sort by prefix alphabetically, regardless of which prefixes appear
- **WHEN** a series contains alphanumeric card numbers with more than two distinct prefixes (e.g. `LE1`, `LE3`, `OTHER1`, `XXL1`, `XXL2`)
- **THEN** they are ordered by prefix alphabetically (`LE` before `OTHER` before `XXL`), and within the same prefix by their numeric suffix ascending

#### Scenario: A category that starts later in the card-number range does not reorder earlier cards
- **WHEN** a series has a category whose lowest card number (e.g. `101`) is higher than another category's lowest card number (e.g. `1`), and the two category names would sort in the opposite order alphabetically (e.g. "Action Cards" before "Heroes")
- **THEN** the response is still ordered by card number, so the card numbered `1` appears before the card numbered `101` regardless of which category name sorts first alphabetically
