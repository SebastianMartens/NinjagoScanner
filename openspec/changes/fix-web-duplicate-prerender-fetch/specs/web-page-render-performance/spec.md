## Purpose

Ensures a page in `NinjagoScanner.Web` fetches its data exactly once per
user navigation, so backend load and time-to-interactive scale with actual
data volume rather than being doubled by Blazor Server's prerender-then-
reconnect render cycle.

## ADDED Requirements

### Requirement: Single data fetch per navigation
Each interactive page in `NinjagoScanner.Web` SHALL fetch its data exactly
once when a person navigates to it, regardless of Blazor Server's render
lifecycle (static prerender pass vs. interactive circuit connection).

#### Scenario: Navigating to a data-driven page
- **WHEN** a person navigates to a page that loads data from
  `CatalogServiceClient` and/or `PictureServiceClient` (Collection,
  Gallery, Overview, Review, Upload)
- **THEN** exactly one set of backend calls is made to satisfy that page
  load, not two

#### Scenario: Verifying via a trace
- **WHEN** a person loads one of these pages while distributed tracing is
  active
- **THEN** the resulting trace shows one cluster of backend calls for the
  page load, not two duplicate clusters
