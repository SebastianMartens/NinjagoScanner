## MODIFIED Requirements

### Requirement: Photo display URLs load without one request per photo
Building the review page's group list SHALL resolve every displayed photo's download URL without issuing one download-URL request per photo in the collection. At most one bounded follow-up request to PictureService, scoped to the photos in the currently displayed group, SHALL be made to resolve their download URLs — never a separate request per photo, and never a request covering photos outside the displayed group — so the page's load time does not grow linearly with the number of photos being reviewed. A photo that already has a resolved download URL SHALL NOT be included in a further such request, per the "Photo display URLs stay stable while the user works on the page" requirement.

#### Scenario: Loading the review page with many photos across many groups
- **WHEN** the review page loads its group list for a collection containing hundreds of photos
- **THEN** at most one bounded request to PictureService is made to resolve the initially displayed group's photos' download URLs, and no request is made to resolve a download URL for any photo outside that group

#### Scenario: Navigating to a different group resolves only that group's URLs
- **WHEN** a user navigates to a group whose photos have not previously had their download URLs resolved
- **THEN** at most one bounded request to PictureService is made to resolve exactly that group's photos' download URLs

#### Scenario: A photo that enters the displayed group via a local edit gets its URL resolved
- **WHEN** a photo is reassigned into the currently displayed group by a local edit (such as a series or card number change) and did not previously have a resolved download URL
- **THEN** a bounded request resolves that photo's download URL without re-requesting a URL for any other photo already displayed in the group
