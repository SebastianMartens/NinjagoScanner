## ADDED Requirements

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
