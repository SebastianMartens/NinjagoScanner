## ADDED Requirements

### Requirement: A single photo can be re-analyzed on demand
Each photo tile SHALL provide a re-analysis control that asks PictureService to re-run AI Analysis on that one photo, acting on no other photo. Activating it SHALL NOT require a confirmation dialog. While that photo's re-analysis is running, the control SHALL show that an analysis is in progress, and every control on that photo's tile that changes the photo (review status, series, card number, language, delete, and the re-analysis control itself) SHALL be disabled; other photo tiles SHALL remain usable, including starting their own re-analysis. Re-analysis SHALL NOT change the photo's `ReviewStatus`.

#### Scenario: Every tile offers a re-analysis control
- **WHEN** a group is displayed
- **THEN** every displayed photo tile shows a re-analysis control, regardless of that photo's `AnalysisStatus` or `ReviewStatus`

#### Scenario: Starting a re-analysis
- **WHEN** a user activates the re-analysis control on a photo tile
- **THEN** a re-analysis of only that photo starts immediately, that tile's re-analysis control shows an in-progress state, and that tile's other change-making controls are disabled until it finishes

#### Scenario: Other tiles stay usable during a re-analysis
- **WHEN** one photo's re-analysis is running
- **THEN** the other photo tiles' controls remain enabled, and a user can start a re-analysis on another tile

#### Scenario: Review status is not changed by re-analysis
- **WHEN** a re-analysis completes for a photo whose `ReviewStatus` is `verified`
- **THEN** the photo's `ReviewStatus` is still `verified`

### Requirement: The review page reflects a finished re-analysis without a reload
When a photo's re-analysis succeeds, the review page SHALL reload its group list so that the photo's tile shows the newly written series name, card name, card number, rarity, language, and `AnalysisStatus`, re-initializing that photo's card number and language controls to the new values. If the photo's details section is expanded, or is expanded later, it SHALL show the new analysis result rather than a previously loaded one. If the new `SetName`/`CardNumber` resolve to a different catalog card, the photo SHALL appear under the group for that card on the reload and the page SHALL stay on the group it was showing when that group still exists, otherwise on the nearest remaining group, using the same navigation rule as any other photo change.

#### Scenario: Re-analysis corrects a misread card number
- **WHEN** a re-analysis of an `unreviewed` photo completes with a different card number than before
- **THEN** the tile shows the new card number in its label and card number control, and the photo appears under the group matching its new series and card number on the reloaded list

#### Scenario: Expanded details show the new result
- **WHEN** a photo's details are expanded and its re-analysis completes
- **THEN** the details section shows the new scanned-at timestamp, error message, and attributes data, not the values from before the re-analysis

#### Scenario: A re-analysis that leaves the match unchanged
- **WHEN** a re-analysis completes and the photo still resolves to the same catalog card
- **THEN** the page stays on the same group and the photo stays in it

### Requirement: A failed re-analysis request is reported on the tile
If a photo's re-analysis request fails - PictureService is unreachable, Gemini or CatalogService is unavailable, or no API key is configured - the review page SHALL leave that photo's displayed data unchanged, re-enable the tile's controls, and show an error message on that photo's tile stating that the re-analysis could not be performed. The message SHALL be cleared when the user starts another re-analysis of that photo. A re-analysis that completes but records `AnalysisStatus` `failed` (an unusable Gemini response) is a successful request and is shown through the tile's normal analysis status and details, not through this error message.

#### Scenario: Gemini is unavailable
- **WHEN** a user activates the re-analysis control and PictureService reports that the analysis could not be run
- **THEN** the photo's tile keeps its previous data, shows an error message that the re-analysis failed, and its controls are enabled again

#### Scenario: Retrying clears the error
- **WHEN** a photo's tile shows a re-analysis error message and the user activates the re-analysis control again
- **THEN** the error message is removed while the new re-analysis is running

#### Scenario: Other tiles are unaffected by one tile's error
- **WHEN** one photo's re-analysis request fails
- **THEN** no other photo tile shows an error message or changes its data
