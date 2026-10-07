# web-photo-rotation Specification

## Purpose

Lets a person fix a photo's upside-down orientation from the Review page, and ensures that correction is reflected consistently wherever that photo is shown in the Web app.

## Requirements

### Requirement: Review page provides a 180° rotate control
The Review page SHALL provide a control, per photo, that toggles the photo's stored rotation flag between normal and rotated 180°.

#### Scenario: Flipping an upside-down photo
- **WHEN** a user activates the rotate control for a photo on the Review page
- **THEN** the photo's rotation flag is toggled and the photo immediately re-renders rotated 180° from its previous orientation

#### Scenario: Flipping a photo back
- **WHEN** a user activates the rotate control a second time for the same photo
- **THEN** the photo's rotation flag is toggled back and the photo renders in its original orientation

### Requirement: Rotation is display-only
Toggling a photo's rotation flag SHALL NOT modify the photo's stored bytes, SHALL NOT change its `AnalysisStatus`, and SHALL NOT trigger re-analysis.

#### Scenario: Rotating does not affect analysis state
- **WHEN** a user toggles a photo's rotation flag
- **THEN** the photo's `AnalysisStatus` and all other sidecar fields besides the rotation flag are unchanged, and no new analysis is triggered

### Requirement: Rotation is applied consistently everywhere a photo is rendered
Every Web view that renders a photo (Review, gallery, collection detail) SHALL display it rotated 180° when its stored rotation flag is set, and SHALL display it in its original orientation otherwise. (The Overview page shows only per-card ownership counts, never a photo image, so this requirement does not apply to it.)

#### Scenario: A rotated photo appears correctly oriented in the gallery
- **WHEN** a photo's rotation flag is set and the gallery page renders it
- **THEN** the photo is displayed rotated 180° from its stored bytes

#### Scenario: A rotated photo appears correctly oriented in the collection detail view
- **WHEN** a photo's rotation flag is set and a card's detail view renders it
- **THEN** the photo is displayed rotated 180° from its stored bytes

#### Scenario: An unrotated photo renders unchanged
- **WHEN** a photo's rotation flag is not set
- **THEN** every view renders it in its original, unrotated orientation
