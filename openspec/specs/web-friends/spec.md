# web-friends Specification

## Purpose

Lets registered users find each other and form mutual friendships, which is the prerequisite for sharing collections and trading cards.

## Requirements

### Requirement: Users can find other users by username
The Web app SHALL let a signed-in user search other registered users by username (case-insensitive, prefix or substring match), excluding themselves, and SHALL show only the username and an indication of the current relationship (none, request sent, request received, friends). Search SHALL NOT expose email addresses or other account data.

#### Scenario: Search finds a user
- **WHEN** a signed-in user searches for "lloyd" and a user named "Lloyd77" exists
- **THEN** the result list includes "Lloyd77" with the relationship state and no email address

#### Scenario: Own account excluded
- **WHEN** a user searches for a string matching their own username
- **THEN** their own account is not in the results

#### Scenario: Short query rejected
- **WHEN** the search query has fewer than 2 characters
- **THEN** no search is performed and no users are listed

### Requirement: Friend request lifecycle
A user SHALL be able to send a friend request to another user, and the recipient SHALL be able to accept or decline it, while the sender SHALL be able to cancel it while pending. A friendship exists only after acceptance and is symmetric. At most one open relationship (pending request or friendship) SHALL exist between any two users.

#### Scenario: Send and accept
- **WHEN** user A sends a request to user B and B accepts it
- **THEN** A and B are friends and each appears in the other's friend list

#### Scenario: Duplicate request rejected
- **WHEN** user A sends a request to B while a request or friendship between A and B already exists (in either direction)
- **THEN** no second record is created and the user is told the relationship already exists

#### Scenario: Mutual pending requests resolve
- **WHEN** A has a pending request to B and B sends a request to A
- **THEN** the two users become friends instead of creating a second pending request

#### Scenario: Cannot befriend self
- **WHEN** a user attempts to send a request to themselves
- **THEN** the request is rejected

#### Scenario: Decline or cancel
- **WHEN** B declines A's pending request, or A cancels it
- **THEN** the request is removed and A and B are not friends

### Requirement: Friends can be removed
Either friend SHALL be able to remove an existing friendship, which immediately revokes all sharing between them. Open trade proposals between the two SHALL be cancelled. Past trade log entries SHALL remain.

#### Scenario: Remove friendship
- **WHEN** A removes B as a friend
- **THEN** B can no longer view A's collection or achievements, A cannot be proposed trades by B, and pending trades between them are cancelled

### Requirement: Friend list and pending requests page
The `/friends` page SHALL list friends, incoming requests, and outgoing requests, with German UI text, and SHALL require sign-in.

#### Scenario: Anonymous access
- **WHEN** an unauthenticated visitor opens `/friends`
- **THEN** they are redirected to sign-in
