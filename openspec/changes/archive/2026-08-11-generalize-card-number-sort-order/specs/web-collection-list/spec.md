## ADDED Requirements

### Requirement: Sorting the table by the Nr. column follows the canonical card-number order
Clicking the "Nr." column header to sort the card list SHALL order rows using the same card-number rule used everywhere else in the application: purely numeric card numbers first, ordered by value; then alphabetic-prefix-plus-number card numbers (e.g. `LE4`, `XXL1`), ordered by prefix alphabetically and then by numeric suffix; then any remaining format ordered alphabetically by raw text. Toggling the sort direction SHALL reverse this order.

#### Scenario: Sorting by Nr. ascending
- **WHEN** a user clicks the "Nr." column header on a list containing both numeric and alphanumeric card numbers
- **THEN** rows are ordered with all numeric card numbers first by ascending value, followed by alphanumeric card numbers ordered by prefix alphabetically and then by numeric suffix

#### Scenario: Toggling sort direction
- **WHEN** a user clicks the "Nr." column header a second time
- **THEN** the same ordering rule applies in reverse
