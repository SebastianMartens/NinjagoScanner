## REMOVED Requirements

### Requirement: Stateless request handling
**Reason**: `NinjagoScanner.Web.Bff` is retired; `NinjagoScanner.Web` reverts to Blazor Server, which inherently holds per-circuit state on the server. Statelessness was only ever a requirement because Lambda demanded it — that constraint no longer exists once compute runs on Fly's always-on machines.
**Migration**: None. There is no replacement requirement — a Blazor Server app holding session state is the intended design, not a gap.

### Requirement: JSON contract to the client, gRPC to backend services
**Reason**: There is no separate WASM client and no JSON API surface between it and a server. `NinjagoScanner.Web` calls CatalogService and PictureService over gRPC directly from server-rendered page code.
**Migration**: None — callers of the old JSON API no longer exist (the WASM client is retired in this same change).

### Requirement: Internal services are not directly reachable from the browser
**Reason**: This requirement's substance is preserved, not dropped — CatalogService and PictureService remain unreachable from the browser under the new architecture too (Fly 6PN keeps them off any public IP). It's removed here specifically because it was scoped as part of the BFF's contract, and the BFF no longer exists to hold it.
**Migration**: The underlying guarantee (browser has no direct network path to CatalogService/PictureService) is expected to be re-asserted as part of whichever capability ends up describing `NinjagoScanner.Web`'s server-side architecture, if one is written; not restated here to avoid inventing a capability this change doesn't otherwise need.
