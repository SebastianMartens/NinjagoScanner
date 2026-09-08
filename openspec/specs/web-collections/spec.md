# web-collections Specification

## Purpose

Gives every user account a private Collection of card photos and sidecars, with an Owner role enforced on every page and action, and a Reader role reserved in the data model for read-only sharing added in a later change.

## Requirements

### Requirement: Collection created at registration
The system SHALL create exactly one Collection and record the newly registered user as its Owner, as part of the same registration operation that creates the user's account. This SHALL happen atomically with account creation — if either step fails, neither the account nor the Collection is created.

#### Scenario: Successful registration creates a Collection
- **WHEN** a visitor successfully registers a new account
- **THEN** a new Collection is created and a Collection Membership record is created for the new account with Role `Owner`

#### Scenario: Registration failure creates neither
- **WHEN** account registration fails (e.g. duplicate username, weak password)
- **THEN** no Collection or Collection Membership record is created

### Requirement: Collection Membership model supports Owner and Reader roles
The system SHALL persist Collection Membership records associating a user with a Collection and a Role of either `Owner` or `Reader`. Only `Owner` is checked for authorization by this change; `Reader` values MAY be stored but no capability in this change grants a `Reader` role or changes behavior based on it.

#### Scenario: Membership role is persisted
- **WHEN** a Collection Membership record is created
- **THEN** its Role is stored as either `Owner` or `Reader`

### Requirement: A Collection has exactly one Owner at a time in this change
Nothing in this change SHALL create more than one `Owner` Collection Membership for the same Collection, and nothing in this change SHALL create a Collection Membership for any user other than the Collection's creator. (Sharing a Collection with additional members is out of scope for this change.)

#### Scenario: A newly created Collection has one member
- **WHEN** a Collection is created during registration
- **THEN** it has exactly one Collection Membership record, for the registering user, with Role `Owner`

### Requirement: Every page and action requires the Owner role on the current collection
The system SHALL determine the current collection for each request (see "Current collection resolution" below) and SHALL require the acting user to hold the `Owner` Collection Membership role on that collection before rendering any page or executing any action, in addition to the existing authentication requirement.

#### Scenario: Owner accesses their own collection's data
- **WHEN** an authenticated user whose Collection Membership role is `Owner` on their current collection requests a page or action
- **THEN** the page renders or the action executes normally

#### Scenario: No Owner membership on the resolved collection
- **WHEN** an authenticated user's Collection Membership role on the current collection is not `Owner` (including when no membership record exists at all)
- **THEN** the system denies the page render or action

### Requirement: Current collection resolution defaults to the user's own collection
The system SHALL resolve "the current collection" for a request via a live query of the acting user's Collection Membership records, defaulting to the collection where that user holds the `Owner` role, rather than from a value cached in the authentication cookie or its claims.

#### Scenario: Live lookup reflects current membership
- **WHEN** the current collection is resolved for a request
- **THEN** the resolution is based on a Collection Membership query performed for that request, not on a value set at login time

### Requirement: Navigation displays the current collection
The Web navigation SHALL display the name of the current user's current collection.

#### Scenario: Navigation shows collection name
- **WHEN** an authenticated user views any page
- **THEN** the navigation displays the name of their current collection
