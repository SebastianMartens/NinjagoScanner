## ADDED Requirements

### Requirement: The series controls include every catalog series in catalog order
The series-reassignment controls on each photo tile SHALL include a control for every series the catalog returns, including "Serie 0", in the catalog's series order (so "Serie 0" comes first). A series without a known logo SHALL be shown as a text-only control. Activating the "Serie 0" control SHALL behave like any other series control: it updates only that photo's `SetName` to "Serie 0".

#### Scenario: Series 0 can be assigned from a photo tile
- **WHEN** a user activates the "Serie 0" control on a photo tile
- **THEN** that photo's `SetName` is saved as "Serie 0", its `ReviewStatus` and every other sidecar field are unchanged, and the tile shows "Serie 0" as its series

#### Scenario: Series 0 control is text-only and listed first
- **WHEN** a photo tile is displayed and the catalog contains "Serie 0"
- **THEN** the tile's series controls start with a text-only "Serie 0" control, followed by the other series' controls

#### Scenario: A photo assigned to Series 0 with a matching catalog card
- **WHEN** a photo is assigned to "Serie 0" and its card number matches a "Serie 0" catalog card (e.g. `5` or `WB5`)
- **THEN** its `SetName` is saved as "Serie 0" and the photo appears in the review group for that catalog card

#### Scenario: A photo assigned to Series 0 without a matching catalog card
- **WHEN** a photo is assigned to "Serie 0" and its card number matches no "Serie 0" catalog card
- **THEN** its `SetName` is saved as "Serie 0" and the photo stays in the group for photos without a known card, until its card number matches a catalog card
