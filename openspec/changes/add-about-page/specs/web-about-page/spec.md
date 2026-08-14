## Purpose

Gives people using NinjagoScanner an in-app explanation of what the app does and the cost, safety, privacy, and usage terms that apply to it, without requiring them to read the repository's README.

## ADDED Requirements

### Requirement: About Page Availability
The Web app SHALL expose a static, read-only About page at the route `/about`, rendered in German only.

#### Scenario: Navigating directly to the About page
- **WHEN** a person navigates to `/about` in the Web app
- **THEN** the About page renders successfully without requiring any backend service (CatalogService or PictureService) to be reachable

### Requirement: About Page Introduction Content
The About page SHALL include an introductory section, in German, explaining what NinjagoScanner does and its main features (automatic card recognition from a photo, viewing the collection, checking owned/missing cards, correcting a misidentified card, and uploading photos from a phone).

#### Scenario: Reading the introduction
- **WHEN** a person opens the About page
- **THEN** they see a German-language section describing NinjagoScanner's purpose and its main features, consistent with the introduction in `readme_de.md`

### Requirement: About Page Cost, Safety, Privacy, and Usage Disclosures
The About page SHALL include a "Ist das kostenlos?" section containing, verbatim, the following disclosures: that NinjagoScanner is a free, non-commercial hobby project funded by minimal non-intrusive ads; that the app is child-appropriate (no violence, chat, in-app purchases, or hidden costs); that no personal data is required and anonymous registration is possible; that no personal data is stored or shared with third parties; that the operator reserves the right to discontinue or restrict the service at any time; that there is no guarantee of availability or of the correctness of recognized cards; and that uploaded photos must be of the uploader's own trading cards only and must not contain personal information.

#### Scenario: Reading the cost and usage disclosures
- **WHEN** a person opens the About page
- **THEN** they see the "Ist das kostenlos?" heading followed by the exact disclosure text about cost, child-safety, data privacy, service availability, accuracy, and photo-upload restrictions

### Requirement: About Page Navigation Entry
The Web app's navigation SHALL include a link to the About page (labeled "Über"), available in both the top header navigation and the bottom mobile tab bar, consistent with the app's existing navigation entries.

#### Scenario: Reaching the About page from navigation
- **WHEN** a person selects the "Über" entry in either the top header nav or the bottom mobile tab bar
- **THEN** they are taken to `/about`

#### Scenario: Active state while on the About page
- **WHEN** a person is on `/about`
- **THEN** the "Über" nav entry is highlighted as the active link, consistent with how other pages highlight their nav entry
