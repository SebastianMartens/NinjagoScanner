## Purpose

Gives the Blazor WASM client a stateless HTTP/JSON API to reach catalog and photo functionality, without any server-held session state between requests.

## ADDED Requirements

### Requirement: Stateless request handling
The BFF SHALL process each request independently, without relying on server-side session state held between requests.

#### Scenario: Requests served without session affinity
- **WHEN** two consecutive requests from the same client are handled by different BFF instances
- **THEN** both requests succeed identically, with no loss of functionality or data

### Requirement: JSON contract to the client, gRPC to backend services
The BFF SHALL expose its API to the WASM client as HTTP/JSON and SHALL translate those requests into gRPC calls to CatalogService and PictureService.

#### Scenario: Client fetches catalog data
- **WHEN** the WASM client requests series/card data from the BFF
- **THEN** the BFF calls CatalogService over gRPC and returns the result to the client as JSON

### Requirement: Internal services are not directly reachable from the browser
The system SHALL NOT expose CatalogService's or PictureService's gRPC endpoints directly to the browser; all browser-originated requests SHALL go through the BFF.

#### Scenario: Browser has no direct network path to internal services
- **WHEN** the WASM client is loaded in a browser
- **THEN** the only backend network endpoint reachable from that browser is the BFF's public API
