## ADDED Requirements

### Requirement: Photo changes update the review page from local data without re-fetching the collection
After a change to a photo is saved successfully - review status, series, card number, language, deletion, or re-analysis - the review page SHALL apply that change to the data it already holds and regroup from it, and SHALL NOT re-fetch the collection's cards or the catalog from the backend services to do so. The page SHALL load the collection from the backend when it is first opened and when the user restarts from the beginning of the list; it SHALL NOT otherwise re-fetch it in response to user actions. A change that fails to save SHALL leave the affected photo's displayed data unchanged.

#### Scenario: Changing a photo does not re-fetch the collection
- **WHEN** a user changes one photo's review status, series, card number, or language and the save succeeds
- **THEN** the photo's tile and its group reflect the new value without any request to list the collection's cards or the catalog

#### Scenario: A failed save leaves the photo unchanged
- **WHEN** saving a change to a photo fails
- **THEN** the photo's tile keeps showing its previous values and no other photo changes

#### Scenario: Restarting from the beginning re-syncs with the server
- **WHEN** a user activates the control to start again from the beginning of the list
- **THEN** the page loads the collection and catalog from the backend again and shows the first matching group of that fresh data

### Requirement: Photo display URLs stay stable while the user works on the page
A photo's display URL, once loaded, SHALL NOT change as a result of the user's actions on the review page, so that the browser does not have to download an already-displayed photo again after a status, series, card number, language, re-analysis, or deletion change.

#### Scenario: Photos are not re-downloaded after a change
- **WHEN** a user changes one photo in the displayed group and the save succeeds
- **THEN** every photo tile in that group keeps the same display URL it had before the change

#### Scenario: A re-analyzed photo keeps its display URL
- **WHEN** a photo's re-analysis succeeds
- **THEN** the photo's tile shows the new analysis data with the display URL it had before the re-analysis

## MODIFIED Requirements

### Requirement: A single photo can be reassigned to a different series via a popover
Each photo tile SHALL display, always visible with no trigger or popover step, a grid of controls listing every known catalog series; activating a series control SHALL update only that photo's `SetName` to the selected series, leaving its `ReviewStatus` and every other sidecar field unchanged.

#### Scenario: Reassigning a misclassified photo
- **WHEN** a user activates a series control for a series different from the group the photo is currently shown in
- **THEN** that photo's `SetName` is updated to the selected series, its `ReviewStatus` is unchanged, and once the save completes the photo no longer appears in the current group and appears under the group matching its new `SetName` and `CardNumber`

#### Scenario: Series controls are visible without any extra step
- **WHEN** a photo tile is displayed
- **THEN** every known catalog series' control is visible on that tile with no action required to reveal them

### Requirement: A single photo's card number can be corrected inline
Each photo tile SHALL provide an inline control for editing that photo's `CardNumber`, pre-filled with its current value. Submitting the control SHALL update only that photo's `CardNumber`, leaving its `ReviewStatus`, `SetName`, and every other sidecar field unchanged.

#### Scenario: Correcting a misdetected card number
- **WHEN** a user edits the card number control on a photo tile to a different value and submits it
- **THEN** that photo's `CardNumber` is updated to the entered value, its `ReviewStatus` and `SetName` are unchanged, and once the save completes the photo appears under the group matching its new `SetName`/`CardNumber`

#### Scenario: Submitting an unchanged card number
- **WHEN** a user submits the card number control without changing its value
- **THEN** that photo's `CardNumber` is unchanged and the photo remains in its current group

#### Scenario: Clearing a card number
- **WHEN** a user clears the card number control and submits it
- **THEN** that photo's `CardNumber` becomes blank, and once the save completes the photo is shown in the catch-all group, since a blank `CardNumber` does not resolve to any catalog card

### Requirement: The card number control reflects the photo's current value after other changes
A photo tile's card number control SHALL show that photo's current `CardNumber` after any change to the page's data, including actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's card number and the save completes
- **THEN** the card number control on that photo tile displays the newly saved value

### Requirement: The language control reflects the photo's current value after other changes
A photo tile's language control SHALL show that photo's current `Language` after any change to the page's data, including actions taken on other photos in the same group.

#### Scenario: Control shows the corrected value after saving
- **WHEN** a user corrects a photo's language and the save completes
- **THEN** the language control on that photo tile displays the newly saved value

### Requirement: A group can be confirmed all at once
The review page SHALL provide a group-level "Confirm all" control that sets `ReviewStatus` to `verified` for every photo currently shown in the group whose `ReviewStatus` is not already `verified` - including photos marked `incorrect` - and saves only those photos; photos that are already `verified` SHALL NOT be saved again. It SHALL then advance the page to the next group among the groups matching the active review-status filter, re-evaluated after the status change. If a save fails part-way, the page SHALL show as `verified` exactly those photos whose save succeeded and SHALL NOT show any photo as `verified` whose save did not succeed.

#### Scenario: Confirming a group where every photo is correct
- **WHEN** a user activates "Confirm all" on a group
- **THEN** every photo currently shown in that group has its `ReviewStatus` set to `verified`, and the page advances to the next group, in sort order, among groups matching the active filter

#### Scenario: Only photos that are not yet verified are saved
- **WHEN** a user activates "Confirm all" on a group in which some photos are already `verified` and others are `unreviewed` or `incorrect`
- **THEN** only the `unreviewed` and `incorrect` photos are saved with `ReviewStatus` `verified`, and the already-`verified` photos are not saved again

#### Scenario: Confirming a group that is already fully verified
- **WHEN** a user activates "Confirm all" on a group in which every displayed photo is already `verified`
- **THEN** nothing is saved and the page advances to the next group

#### Scenario: Confirm all advances even into an already-reviewed group
- **WHEN** the next group in sort order after a "Confirm all" action has every photo already reviewed and still matches the active filter
- **THEN** the page still advances to that group rather than skipping it

#### Scenario: Confirm all under an active filter can empty the group list
- **WHEN** the active filter is `Unreviewed`, the confirmed group was the only remaining group matching the filter, and no later group matches it either
- **THEN** the page shows the same empty state used when there is nothing left to review

#### Scenario: A save fails part-way through Confirm all
- **WHEN** saving one of several photos fails during "Confirm all"
- **THEN** the photos whose saves succeeded show as `verified`, the failed photo and any photo not yet saved keep their previous `ReviewStatus`, and the page does not present the group as fully confirmed

### Requirement: Deleting a photo keeps group navigation consistent
Deleting a photo SHALL remove it from every place the review page shows it once the deletion succeeds. If the deleted photo was not the last one in its group, the remaining photos of that group SHALL stay visible and the page SHALL stay on the same group. If the deleted photo was the last one in its group, that group SHALL no longer appear, and the page SHALL advance to the next group among the groups matching the active review-status filter, or show the same empty state used when there is nothing left to review if none remain.

#### Scenario: Deleting one of several photos in a group
- **WHEN** a user confirms deletion of a photo and other photos remain in its group
- **THEN** the deleted photo no longer appears, the group's remaining photos are unaffected, and the page stays on that group

#### Scenario: Deleting the last photo in a group
- **WHEN** a user confirms deletion of the only remaining photo in the currently displayed group
- **THEN** that group no longer appears in the list and the page advances to the next group among the groups matching the active filter

#### Scenario: Deleting the last remaining photo overall
- **WHEN** a user confirms deletion of a photo and no other group matches the active filter afterward
- **THEN** the review page shows the same empty state used when there is nothing left to review

### Requirement: The review page reflects a finished re-analysis without a reload
When a photo's re-analysis succeeds, the review page SHALL update that photo's tile to show the newly written series name, card name, card number, rarity, language, and `AnalysisStatus`, updating that photo's card number and language controls to the new values. If the photo's details section is expanded, or is expanded later, it SHALL show the new analysis result rather than a previously loaded one. If the new `SetName`/`CardNumber` resolve to a different catalog card, the photo SHALL appear under the group for that card and the page SHALL stay on the group it was showing when that group still exists, otherwise on the nearest remaining group, using the same navigation rule as any other photo change.

#### Scenario: Re-analysis corrects a misread card number
- **WHEN** a re-analysis of an `unreviewed` photo completes with a different card number than before
- **THEN** the tile shows the new card number in its label and card number control, and the photo appears under the group matching its new series and card number

#### Scenario: Expanded details show the new result
- **WHEN** a photo's details are expanded and its re-analysis completes
- **THEN** the details section shows the new scanned-at timestamp, error message, and attributes data, not the values from before the re-analysis

#### Scenario: A re-analysis that leaves the match unchanged
- **WHEN** a re-analysis completes and the photo still resolves to the same catalog card
- **THEN** the page stays on the same group and the photo stays in it
