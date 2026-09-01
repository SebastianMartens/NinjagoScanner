## Purpose

Protects the Web app behind username/password authentication so only registered users can access the application, and provides self-registration for new users.

## ADDED Requirements

### Requirement: User registration
The system SHALL allow visitors to create an account by providing a username and a password. The username MUST NOT be required to be an email address. The username MUST be unique (case-insensitive). The password MUST meet a minimum complexity policy (at least 6 characters, at least one digit, at least one non-alphanumeric character).

#### Scenario: Successful registration
- **WHEN** a visitor submits the registration form with a valid, unused username and a compliant password
- **THEN** the system creates the account and redirects the visitor to the login page

#### Scenario: Duplicate username
- **WHEN** a visitor submits a username that already exists (case-insensitive)
- **THEN** the system rejects the registration and displays an error message

#### Scenario: Weak password
- **WHEN** a visitor submits a password that does not meet the complexity policy
- **THEN** the system rejects the registration and displays which requirements are not met

### Requirement: User login
The system SHALL allow registered users to authenticate with their username and password. Authentication SHALL be maintained via a server-side cookie.

#### Scenario: Successful login
- **WHEN** a user submits valid credentials on the login page
- **THEN** the system issues an authentication cookie and redirects the user to the home page

#### Scenario: Invalid credentials
- **WHEN** a user submits an incorrect username or password
- **THEN** the system displays a generic error message without revealing which field was wrong

#### Scenario: Locked-out account
- **WHEN** a user's account is locked due to repeated failed login attempts
- **THEN** the system displays a message indicating the account is temporarily locked

### Requirement: Account lockout
The system SHALL lock a user account after 5 consecutive failed login attempts. The lockout SHALL last 15 minutes. Successful login resets the failed-attempt counter.

#### Scenario: Lockout triggered
- **WHEN** a user fails to log in 5 times in a row
- **THEN** the account is locked and further login attempts are rejected for 15 minutes

#### Scenario: Lockout expires
- **WHEN** 15 minutes have passed since lockout
- **THEN** the user can attempt to log in again

### Requirement: Route authorization
All pages in the Web app SHALL require an authenticated user. Unauthenticated requests SHALL be redirected to the login page. The login and registration pages themselves MUST be accessible without authentication.

#### Scenario: Unauthenticated access to protected page
- **WHEN** an unauthenticated visitor navigates to any page other than login or register
- **THEN** the system redirects them to the login page

#### Scenario: Authenticated access
- **WHEN** an authenticated user navigates to any page
- **THEN** the page renders normally

### Requirement: Logout
The system SHALL provide a logout mechanism accessible from the navigation. Logging out SHALL clear the authentication cookie and redirect to the login page.

#### Scenario: User logs out
- **WHEN** an authenticated user activates the logout action
- **THEN** the authentication cookie is removed and the user is redirected to the login page

### Requirement: User persistence
User accounts SHALL be persisted in a SQLite database. The database file SHALL survive application restarts and redeployments (Fly volume).

#### Scenario: Account survives restart
- **WHEN** the application restarts
- **THEN** previously registered accounts remain valid and users can log in with their existing credentials
