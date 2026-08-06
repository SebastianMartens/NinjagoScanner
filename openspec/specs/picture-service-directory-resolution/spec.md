# picture-service-directory-resolution Specification

## Purpose

Defines how PictureService resolves the card photos directory (and related scanner configuration) shared by all of its RPCs, from an explicit per-request value down to environment-derived defaults.

## Requirements

### Requirement: Directory resolution follows a fixed precedence order
The card photos directory used by any RPC SHALL be resolved in this order: (1) an explicit per-request directory override, if provided; (2) the `CardPhotos:Directory` configuration key or `CARD_PHOTOS_DIRECTORY` environment variable; (3) the first existing directory among an ordered list of default candidate paths; (4) if none of the default candidates exist, the first candidate path regardless of existence.

#### Scenario: Request override takes precedence
- **WHEN** an RPC request specifies a card photos directory override
- **THEN** that directory is used regardless of any configured or default value

#### Scenario: Configuration value used when no override given
- **WHEN** no per-request override is given but `CardPhotos:Directory` or `CARD_PHOTOS_DIRECTORY` is set
- **THEN** the configured value is used

#### Scenario: Falls back to default candidates
- **WHEN** neither a request override nor a configuration value is present
- **THEN** the directory is chosen from the ordered default candidate paths, using the first one that exists on disk

#### Scenario: No default candidate exists
- **WHEN** none of the default candidate paths exist on disk
- **THEN** the first candidate path in the ordered list is used anyway

### Requirement: Default candidates prefer the git main-repo cardFotos folder when run from a worktree
When resolving default candidates, if the process is running from within a git worktree (i.e. its `.git` is a file pointing at a `worktrees/` path rather than a directory), a `cardFotos` folder under the main repository root SHALL be checked before the other default candidates.

#### Scenario: Running from a linked worktree
- **WHEN** the service starts from a directory whose `.git` file points into a `worktrees/<name>` path under the main repository's `.git`
- **THEN** `<main-repo-root>/cardFotos` is probed as a candidate before the executable- and working-directory-relative candidates

#### Scenario: Not running from a worktree
- **WHEN** the process is not running from a linked git worktree (no `.git` file with a `gitdir:` pointer resolvable to a main repo)
- **THEN** the default candidates are limited to the executable- and working-directory-relative paths, without a git-derived candidate

### Requirement: Other scan settings follow the same request-then-config-then-default precedence
Scanner settings other than the directory (API key, model, catalog service address, overwrite flag, delay, retry delay, max attempts, timeout) SHALL each be resolved using a per-request value first, then a named configuration key or environment variable, then a hard-coded default.

#### Scenario: Request-provided model overrides configuration
- **WHEN** a `Scan` request specifies a model name
- **THEN** that model is used instead of any configured `Gemini:Model`/`GEMINI_MODEL` value or the built-in default

#### Scenario: Max attempts and timeout have enforced minimums
- **WHEN** the resolved `max_attempts` or `timeout_seconds` value (from request, configuration, or default) is below the enforced minimum (1 attempt, 10 seconds)
- **THEN** the enforced minimum is used instead of the lower resolved value
