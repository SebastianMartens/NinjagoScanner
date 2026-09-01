# web-auth-rate-limiting Specification

## Purpose
Limits the rate of login and registration requests to protect against brute-force attacks and spam account creation.
## Requirements
### Requirement: Registration rate limit
The system SHALL limit registration requests to a fixed number per IP address per time window. Requests exceeding the limit SHALL be rejected with a 429 (Too Many Requests) response.

#### Scenario: Registration within limit
- **WHEN** a visitor submits a registration request and has not exceeded the rate limit
- **THEN** the request is processed normally

#### Scenario: Registration rate limit exceeded
- **WHEN** a visitor submits a registration request and has exceeded the allowed number of registrations from that IP within the time window
- **THEN** the system rejects the request with a 429 status and displays a "too many attempts" message

### Requirement: Login rate limit
The system SHALL limit login requests to a fixed number per IP address per time window. Requests exceeding the limit SHALL be rejected with a 429 (Too Many Requests) response.

#### Scenario: Login within limit
- **WHEN** a user submits a login request and has not exceeded the rate limit
- **THEN** the request is processed normally

#### Scenario: Login rate limit exceeded
- **WHEN** a visitor submits a login request and has exceeded the allowed number of login attempts from that IP within the time window
- **THEN** the system rejects the request with a 429 status and displays a "too many attempts" message

