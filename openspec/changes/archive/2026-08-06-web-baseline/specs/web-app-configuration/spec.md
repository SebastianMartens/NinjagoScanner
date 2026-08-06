# web-app-configuration Specification

## Purpose

Resolves, at startup, where the Web app finds shared card photos and how it reaches CatalogService and PictureService, using configuration with environment-variable and auto-discovery fallbacks, and exposes the resolved photos directory as static content.

## ADDED Requirements

### Requirement: Card photos directory resolution has a defined precedence
The system SHALL resolve the card photos directory in this order: the `CardPhotos:Directory` configuration key, then `CardPhotosDirectory`, then the `NINJAGO_CARD_PHOTOS_DIR` environment variable, then `CARD_PHOTOS_DIRECTORY`; if none are set, it SHALL search upward from the content root, base directory, current directory, and (if running from a git worktree) the main repository root for a `cardFotos` folder that is not inside a `bin` directory; if still not found, it SHALL fall back to `<contentRoot>/../cardFotos`.

#### Scenario: Explicit configuration wins over auto-discovery
- **WHEN** `CardPhotos:Directory` (or any higher-precedence source) is set
- **THEN** that configured path is used as the card photos directory, resolved to an absolute path relative to the content root if it was relative, regardless of whether a `cardFotos` folder would otherwise be auto-discovered

#### Scenario: No configuration set, folder discoverable
- **WHEN** none of the configuration keys or environment variables are set, and a `cardFotos` folder exists in one of the searched roots outside any `bin` directory
- **THEN** that discovered folder is used as the card photos directory

### Requirement: Service addresses default when unconfigured
CatalogService's address SHALL resolve from `CatalogService:Address` configuration, then the `CATALOG_SERVICE_ADDRESS` environment variable, then default to `http://localhost:5073`. PictureService's address SHALL resolve from `PictureService:Address` configuration, then the `PICTURE_SERVICE_ADDRESS` environment variable, then default to `http://localhost:5169`.

#### Scenario: No service address configured
- **WHEN** neither the configuration key nor the environment variable is set for a service address
- **THEN** the corresponding default localhost address is used

### Requirement: Max upload size defaults and rejects invalid overrides
The maximum upload size SHALL resolve from `CardPhotos:MaxUploadBytes` configuration, then `CardPhotosMaxUploadBytes`, then the `CARD_PHOTOS_MAX_UPLOAD_BYTES` environment variable, parsed as a positive integer; if none are set, or the configured value fails to parse as a positive integer, it SHALL default to 15 MB (15 * 1024 * 1024 bytes).

#### Scenario: Configured value is not a valid positive number
- **WHEN** the configured max upload bytes value is missing, non-numeric, zero, or negative
- **THEN** the default of 15 MB is used instead

### Requirement: Card photos are served as static files only when the directory exists
The system SHALL serve the resolved card photos directory as static files under the `/cardFotos` request path only if that directory exists at startup; it SHALL NOT attempt to register a static file provider for a non-existent directory.

#### Scenario: Card photos directory exists at startup
- **WHEN** the resolved card photos directory exists when the app starts
- **THEN** files within it are served under `/cardFotos/<file>`

#### Scenario: Card photos directory does not exist at startup
- **WHEN** the resolved card photos directory does not exist when the app starts
- **THEN** no static file provider is registered for it, and the app still starts successfully
