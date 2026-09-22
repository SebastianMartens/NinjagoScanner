## ADDED Requirements

### Requirement: Photo display URLs are resolved only for photos matched within the selected series
Building the Gallery page's card grid SHALL resolve download URLs only for the photos matched to a catalog card within the currently selected series, not for every photo in the collection, so the page's load time does not grow with total collection size. Selecting a different series SHALL resolve download URLs only for the newly selected series' matched photos.

#### Scenario: Selecting a series resolves only its own matched photos' URLs
- **WHEN** a user selects a series on the Gallery page
- **THEN** download URLs are resolved only for the photos matched to catalog cards within that series, not for photos matched to cards in any other series

#### Scenario: Switching series does not resolve the whole collection's URLs
- **WHEN** a user switches from one selected series to another
- **THEN** download URLs are resolved only for the newly selected series' matched photos, regardless of how many photos exist elsewhere in the collection
