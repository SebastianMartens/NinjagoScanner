# web-local-seed-mode Specification

## Purpose

Lets developers create a deterministic local Web database (users, friendship, trade) matching the fixture photo collections, without using the real user database.

## Requirements

### Requirement: Seed command
Web SHALL support a `seed` command-line mode that applies migrations to the database at the configured path and creates fixed users with a known password, a friendship between two of them, and a pending trade, using the fixed collection IDs of the fixture data, then exits.

#### Scenario: Seeding a fresh database
- **WHEN** `dotnet run -- seed` runs with `AUTH_DATABASE_PATH` pointing to a non-existent file
- **THEN** the file is created with the seeded users, friendship and pending trade, and the process exits with status 0 without starting the web host

#### Scenario: Re-seeding
- **WHEN** the command runs against an already seeded database
- **THEN** the result is the same seeded state, with no duplicate records

### Requirement: Real user database protected
The seed mode SHALL refuse to run against `Data/users.db` (the default developer database path).

#### Scenario: Default path
- **WHEN** `seed` runs with no configured database path override
- **THEN** it exits non-zero with an explanatory message and modifies nothing
