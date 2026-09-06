## MODIFIED Requirements

### Requirement: UpdateSetName creates a sidecar record that reports as not-analyzed if none exists
If no sidecar file exists yet for the given image, `UpdateSetName` SHALL create one before setting its set name. Until that image is analyzed, its analysis status SHALL be reported as `notAnalyzed` (see `picture-service-card-listing`'s not-analyzed fallback).

#### Scenario: Setting the name of an unscanned image
- **WHEN** `UpdateSetName` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested set name, and subsequently reading that image's card entry reports `AnalysisStatus` `notAnalyzed`

### Requirement: UpdateCardNumber creates a sidecar record that reports as not-analyzed if none exists
If no sidecar file exists yet for the given image, `UpdateCardNumber` SHALL create one before setting its `CardNumber`. Until that image is analyzed, its analysis status SHALL be reported as `notAnalyzed` (see `picture-service-card-listing`'s not-analyzed fallback).

#### Scenario: Correcting the card number of an unscanned image
- **WHEN** `UpdateCardNumber` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested `CardNumber`, and subsequently reading that image's card entry reports `AnalysisStatus` `notAnalyzed`

### Requirement: UpdateCardLanguage creates a sidecar record that reports as not-analyzed if none exists
If no sidecar file exists yet for the given image, `UpdateCardLanguage` SHALL create one before setting its `Language`. Until that image is analyzed, its analysis status SHALL be reported as `notAnalyzed` (see `picture-service-card-listing`'s not-analyzed fallback).

#### Scenario: Setting the language of an unscanned image
- **WHEN** `UpdateCardLanguage` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested `Language`, and subsequently reading that image's card entry reports `AnalysisStatus` `notAnalyzed`

### Requirement: UpdateReviewStatus creates a sidecar record that reports as not-analyzed if none exists
If no sidecar file exists yet for the given image, `UpdateReviewStatus` SHALL create one before setting its `ReviewStatus`. Until that image is analyzed, its analysis status SHALL be reported as `notAnalyzed` (see `picture-service-card-listing`'s not-analyzed fallback).

#### Scenario: Setting the review status of an unscanned image
- **WHEN** `UpdateReviewStatus` is called for an image with no existing sidecar file
- **THEN** a new sidecar file is created with the requested `ReviewStatus`, and subsequently reading that image's card entry reports `AnalysisStatus` `notAnalyzed`
