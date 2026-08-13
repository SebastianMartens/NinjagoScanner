# web-card-review-flow Specification

## Purpose

Lets a person work through scanned card photos one catalog card at a time, comparing every photo grouped under that series/card-number against each other, confirming correct groups in one click, and fixing the dominant error (Gemini picking the wrong series) without leaving the page.

## Requirements

### Requirement: Photos are grouped by series and card number
The review page SHALL group every scanned photo by resolving its own sidecar `SetName` and `CardNumber` to a catalog card, using the same normalization the collection overview uses when matching sidecars against the catalog (differences in letter case, whitespace, and formatting are ignored). A group exists if and only if at least one photo's sidecar values resolve to that catalog card.

Series name and card number uniquely identify a catalog card (see GLOSSARY.md's Card entry), so a group corresponds to exactly one catalog card.

#### Scenario: Photos sharing a series and card number are grouped together
- **WHEN** two or more photos have the same `SetName` and `CardNumber` in their sidecar
- **THEN** they appear together in the same group on the review page

#### Scenario: Photos whose raw sidecar values normalize to the same catalog card are grouped together
- **WHEN** two or more photos have `SetName`/`CardNumber` values that differ only in ways normalization ignores (such as case, whitespace, or leading zeros) but resolve to the same catalog card
- **THEN** they appear together in the same group on the review page

#### Scenario: A catalog card with no photos never appears
- **WHEN** a catalog card has no photo whose sidecar `SetName`/`CardNumber` resolves to it
- **THEN** no group for that card is shown on the review page

### Requirement: Groups are ordered by known series order, then card number
Groups, each corresponding to a matched catalog card, SHALL be ordered by that card's series' catalog `SortOrder`, then by `CardNumber` within the series using the same card-number rule used everywhere else in the application: purely numeric card numbers first ordered by value, then alphabetic-prefix-plus-number card numbers ordered by prefix alphabetically and then by numeric suffix, then anything else ordered alphabetically by raw text. Every photo whose `SetName`/`CardNumber` pair does not resolve, after normalization, to a catalog card - including a blank `SetName`, a blank `CardNumber`, an unrecognized series, or a card number not found within an otherwise recognized series - SHALL be merged into exactly one catch-all group, sorted after every matched group.

#### Scenario: Groups follow catalog series order
- **WHEN** the review page lists matched groups
- **THEN** they appear ordered by the series' catalog `SortOrder`, and by `CardNumber` within the same series

#### Scenario: Unrecognized and blank series are combined into one trailing group
- **WHEN** photos have a `SetName` that does not resolve to any known catalog series, or have no `SetName` at all
- **THEN** all such photos appear together in a single group that is ordered after every matched group

#### Scenario: Numeric and alphanumeric card numbers within the same series order correctly
- **WHEN** a series has matched groups for both purely numeric card numbers (e.g. `2`, `10`) and alphanumeric card numbers (e.g. `LE1`, `XXL1`)
- **THEN** groups for numeric card numbers appear first, ordered by value, followed by groups for alphanumeric card numbers ordered by prefix alphabetically and then by numeric suffix

#### Scenario: A recognized series with an unrecognized card number falls into the catch-all group
- **WHEN** a photo's `SetName` matches a known catalog series but its `CardNumber` does not match any card within that series
- **THEN** that photo is placed in the catch-all group rather than forming its own group

### Requirement: A matched group's header shows the resolved catalog card name
The review page SHALL show, in a matched group's header, the catalog card name resolved from that group's series name and card number, in addition to the existing series name and card number label. The catch-all group, which does not correspond to a single catalog card, SHALL NOT show a catalog card name in its header.

#### Scenario: Viewing a matched group's header
- **WHEN** a user views a group that resolved to a catalog card
- **THEN** the group header shows that catalog card's name together with the series name and card number

#### Scenario: Viewing the catch-all group's header
- **WHEN** a user views the catch-all group
- **THEN** the group header does not show a catalog card name

### Requirement: All photos in a group are shown at once
The review page SHALL display every photo currently in the selected group simultaneously, with each photo tile always showing that photo's own current series name, card name, and card number.

#### Scenario: Viewing a group with multiple photos
- **WHEN** a user opens a group containing more than one photo
- **THEN** all of that group's photos are shown at the same time, each labeled with its own series name, card name, and card number

### Requirement: Additional photo details are collapsed by default
Each photo tile SHALL hide its remaining sidecar fields (rarity, confidence, reasoning summary, detected text, error message, scanned-at timestamp) behind an on-demand control, collapsed by default.

#### Scenario: Expanding a photo's details
- **WHEN** a user activates the details control on a photo tile
- **THEN** that photo's remaining sidecar fields become visible, without affecting other photo tiles

### Requirement: A single photo's review status is set via a three-segment status control
Each photo tile SHALL provide a single control with three segments - `Unreviewed`, `Verified`, and `Incorrect` - that both displays and sets that photo's `ReviewStatus`, acting only on that one photo. The segment matching the photo's current `ReviewStatus` SHALL be visually highlighted, and activating any segment SHALL set that photo's `ReviewStatus` to the corresponding value.

#### Scenario: Current status is highlighted
- **WHEN** a photo tile is displayed
- **THEN** the segment matching that photo's current `ReviewStatus` is visually highlighted, and no other segment is

#### Scenario: Confirming one photo in a group
- **WHEN** a user activates the `Verified` segment on one photo tile
- **THEN** that photo's `ReviewStatus` becomes `verified`, the `Verified` segment becomes highlighted, and no other photo in the group is changed

#### Scenario: Flagging one photo as an error
- **WHEN** a user activates the `Incorrect` segment on one photo tile
- **THEN** that photo's `ReviewStatus` becomes `incorrect`, the `Incorrect` segment becomes highlighted, and no other photo in the group is changed

#### Scenario: Reverting a photo to unreviewed
- **WHEN** a user activates the `Unreviewed` segment on a photo tile whose `ReviewStatus` is currently `verified` or `incorrect`
- **THEN** that photo's `ReviewStatus` becomes `unreviewed`, the `Unreviewed` segment becomes highlighted, and no other photo in the group is changed

### Requirement: The status control reflects ReviewStatus changes made elsewhere on the page
A photo tile's status control SHALL reflect that photo's current `ReviewStatus` immediately after any action that changes it, including group-level actions, without requiring the page to be reloaded.

#### Scenario: Control updates after Confirm all
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo tile in that group has its `Verified` segment highlighted without a page reload

### Requirement: A single photo can be reassigned to a different series via a popover
Each photo tile SHALL provide a single trigger control, showing the photo's current series, that opens a popover grid listing every known catalog series; activating a series within the open popover SHALL update only that photo's `SetName` to the selected series, leaving its `ReviewStatus` and every other sidecar field unchanged, and SHALL close the popover. Activating the trigger control itself SHALL only open or close the popover and SHALL NOT change the photo's `SetName`.

#### Scenario: Reassigning a misclassified photo
- **WHEN** a user opens a photo tile's series popover and activates a series control for a series different from the group the photo is currently shown in
- **THEN** that photo's `SetName` is updated to the selected series, its `ReviewStatus` is unchanged, the popover closes, and the photo no longer appears in the current group on the next load

#### Scenario: Opening and closing the popover does not reassign the photo
- **WHEN** a user activates a photo tile's series trigger control to open or close its popover, without activating a series within it
- **THEN** that photo's `SetName` is unchanged

#### Scenario: The trigger control reflects the photo's current series
- **WHEN** a photo tile is displayed
- **THEN** its series trigger control shows that photo's current series

### Requirement: A single photo's card number can be corrected inline
Each photo tile SHALL provide an inline control for editing that photo's `CardNumber`, pre-filled with its current value. Submitting the control SHALL update only that photo's `CardNumber`, leaving its `ReviewStatus`, `SetName`, and every other sidecar field unchanged.

#### Scenario: Correcting a misdetected card number
- **WHEN** a user edits the card number control on a photo tile to a different value and submits it
- **THEN** that photo's `CardNumber` is updated to the entered value, its `ReviewStatus` and `SetName` are unchanged, and the group list is reloaded so the photo appears under the group matching its new `SetName`/`CardNumber`

#### Scenario: Submitting an unchanged card number
- **WHEN** a user submits the card number control without changing its value
- **THEN** that photo's `CardNumber` is unchanged and the photo remains in its current group

#### Scenario: Clearing a card number
- **WHEN** a user clears the card number control and submits it
- **THEN** that photo's `CardNumber` becomes blank, and the photo moves to the catch-all group on the next load, since a blank `CardNumber` does not resolve to any catalog card

### Requirement: The card number control reflects the photo's current value after other changes
A photo tile's card number control SHALL be re-initialized to that photo's current `CardNumber` whenever the group list is reloaded, including after actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's card number and the save completes
- **THEN** the card number control on that photo tile displays the newly saved value

### Requirement: A single photo's language can be corrected inline
Each photo tile SHALL provide an inline control for editing that photo's `Language`, pre-filled with its current value (German, English, Polish, or Unknown), offering exactly those four options. Selecting a different option SHALL update only that photo's `Language`, leaving its `ReviewStatus`, `SetName`, `CardNumber`, and every other sidecar field unchanged, and SHALL NOT move the photo to a different group, since `Language` is not part of the series/card-number grouping key.

#### Scenario: Correcting a misdetected language
- **WHEN** a user selects a different option in the language control on a photo tile
- **THEN** that photo's `Language` is updated to the selected value, its `ReviewStatus`, `SetName`, and `CardNumber` are unchanged, and the photo remains in its current group

#### Scenario: Language control offers a closed set of options
- **WHEN** a user opens the language control on a photo tile
- **THEN** German, English, Polish, and Unknown are the only selectable options

### Requirement: The language control reflects the photo's current value after other changes
A photo tile's language control SHALL be re-initialized to that photo's current `Language` whenever the group list is reloaded, including after actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's language and the save completes
- **THEN** the language control on that photo tile displays the newly saved value

### Requirement: Groups can be filtered by review status
The review page SHALL provide a review-status filter control offering `All`, `Unreviewed`, `Verified`, and `Incorrect`. When a status other than `All` is selected, a group SHALL be included in the list used for display and navigation if and only if at least one of its photos currently has that `ReviewStatus`; every photo in an included group SHALL still be shown, regardless of that individual photo's own `ReviewStatus`. Selecting `All` includes every group, matching the page's behavior without a filter.

#### Scenario: Filtering to groups with an unreviewed photo
- **WHEN** a user selects `Unreviewed` in the review-status filter
- **THEN** only groups containing at least one photo whose `ReviewStatus` is `unreviewed` are shown, and every photo in each shown group is displayed regardless of its own `ReviewStatus`

#### Scenario: A group matching the filter keeps its differently-reviewed photos visible
- **WHEN** the active filter is `Incorrect` and a matching group also contains photos that are `verified` or `unreviewed`
- **THEN** all of that group's photos remain visible, not only the ones with `ReviewStatus` `incorrect`

#### Scenario: Clearing the filter
- **WHEN** a user selects `All`
- **THEN** every group is shown again, regardless of any photo's `ReviewStatus`

#### Scenario: No groups match the active filter
- **WHEN** the active filter excludes every group
- **THEN** the review page shows the same empty state used when there is nothing left to review

### Requirement: The group list updates to stay consistent with the active filter
Whenever a photo's `ReviewStatus` changes, the review page SHALL re-evaluate the active filter against the updated data before the next group is shown, so a group that no longer has any photo matching the active filter is no longer included in navigation.

#### Scenario: Confirming the last matching photo in a group removes it from a non-All filter
- **WHEN** the active filter is `Unreviewed`, a group has exactly one `unreviewed` photo, and that photo's status is changed to `verified`
- **THEN** that group is no longer included in the filtered group list

### Requirement: A group can be confirmed all at once
The review page SHALL provide a group-level "Confirm all" control that sets `ReviewStatus` to `verified` for every photo currently shown in the group, then advances the page to the next group among the groups matching the active review-status filter, re-evaluated after the status change.

#### Scenario: Confirming a group where every photo is correct
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo currently shown in that group has its `ReviewStatus` set to `verified`, and the page advances to the next group, in sort order, among groups matching the active filter

#### Scenario: Confirm all advances even into an already-reviewed group
- **WHEN** the next group in sort order after a "Confirm all" action has every photo already reviewed and still matches the active filter
- **THEN** the page still advances to that group rather than skipping it

#### Scenario: Confirm all under an active filter can empty the group list
- **WHEN** the active filter is `Unreviewed`, the confirmed group was the only remaining group matching the filter, and no later group matches it either
- **THEN** the page shows the same empty state used when there is nothing left to review

### Requirement: Groups can be navigated manually
The review page SHALL provide controls to move to the previous or next group among the groups currently matching the active review-status filter, in the order defined above, independent of the "Confirm all" action.

#### Scenario: Moving to the next group manually
- **WHEN** a user activates the next-group control
- **THEN** the page displays the next group in sort order among groups matching the active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Moving to the previous group manually
- **WHEN** a user activates the previous-group control
- **THEN** the page displays the previous group in sort order among groups matching the active filter, without changing any photo's `ReviewStatus` or `SetName`

#### Scenario: Changing the filter returns to the first matching group
- **WHEN** a user selects a different review-status filter value
- **THEN** the page displays the first group, in sort order, among the groups matching the newly selected filter

### Requirement: A single photo can be permanently deleted after confirmation
Each photo tile SHALL provide a delete control. Activating it SHALL open a confirmation dialog before any deletion occurs. Canceling the confirmation dialog SHALL leave that photo, its sidecar data, and every other photo unchanged. Confirming the dialog SHALL permanently delete both the photo file and its sidecar file from disk.

#### Scenario: Requesting deletion opens a confirmation dialog
- **WHEN** a user activates the delete control on a photo tile
- **THEN** a confirmation dialog is shown and no file is deleted yet

#### Scenario: Canceling the confirmation dialog keeps the photo
- **WHEN** a user dismisses or cancels the confirmation dialog without confirming
- **THEN** the photo and its sidecar file remain on disk and the group list is unchanged

#### Scenario: Confirming deletion removes the photo and its sidecar from disk
- **WHEN** a user confirms deletion in the dialog
- **THEN** the photo's image file and its sidecar file are permanently removed from disk, and the photo no longer appears in the review page

### Requirement: Deleting a photo keeps group navigation consistent
Deleting a photo SHALL reload the group list so the deleted photo is no longer shown anywhere on the review page. If the deleted photo was not the last one in its group, the remaining photos of that group SHALL stay visible and the page SHALL stay on the same group. If the deleted photo was the last one in its group, that group SHALL no longer appear, and the page SHALL advance to the next group among the groups matching the active review-status filter, or show the same empty state used when there is nothing left to review if none remain.

#### Scenario: Deleting one of several photos in a group
- **WHEN** a user confirms deletion of a photo and other photos remain in its group
- **THEN** the deleted photo no longer appears, the group's remaining photos are unaffected, and the page stays on that group

#### Scenario: Deleting the last photo in a group
- **WHEN** a user confirms deletion of the only remaining photo in the currently displayed group
- **THEN** that group no longer appears in the list and the page advances to the next group among the groups matching the active filter

#### Scenario: Deleting the last remaining photo overall
- **WHEN** a user confirms deletion of a photo and no other group matches the active filter afterward
- **THEN** the review page shows the same empty state used when there is nothing left to review
