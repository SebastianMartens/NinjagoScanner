## Why

The three services (CatalogService, PictureService, Web) only run today as local processes sharing a local disk (`cardFotos/`). To make the app reachable outside this machine, it needs a cloud hosting architecture — and the current design's two implicit assumptions (a shared local filesystem, and Web holding a persistent in-memory session per browser tab) don't survive a move to independently deployable, horizontally-hostable services. Both need to be designed out, not just relocated.

## What Changes

- **BREAKING**: PictureService stops using the local filesystem for photo/sidecar storage. Photos move to S3; sidecar records (analysis status, review status, card match, Gemini output) move to DynamoDB, keyed by a generated photo ID instead of the original filename.
- **BREAKING**: `NinjagoScanner.Web` splits into two deployable projects:
  - `NinjagoScanner.Web.Client` — a Blazor WebAssembly app (static assets), replacing the current Blazor Server / Interactive Server render mode.
  - `NinjagoScanner.Web.Bff` — a stateless HTTP/JSON API (the BFF) that the WASM client calls, which in turn calls CatalogService and PictureService over gRPC. This removes Web's dependency on a persistent server-held session ("circuit"), which was the blocker to hosting it anywhere other than an always-on server process.
- Photo upload changes from streaming bytes through the app server (over Blazor Server's persistent connection) to the browser uploading directly to S3 via a short-lived pre-authorized URL issued by the BFF.
- CatalogService and PictureService remain always-on gRPC services (Fargate), unchanged in role — only their host and PictureService's storage backend change.
- Existing local `cardFotos/` data (~1.6GB, ~13k files) is migrated once, by copy (not move), into S3 + DynamoDB; the local copy stays on disk afterward as an archive the running app no longer reads.
- New CI/CD: GitHub Actions builds/tests on PR, and deploys per-project on push to main using path-filtered workflows (each project redeploys independently, mirroring their current independent-runnability). AWS auth via GitHub OIDC, no long-lived credentials.
- New infrastructure-as-code (Terraform) for everything above: ECS/Fargate services, ALB, Lambda + API Gateway, S3, DynamoDB, CloudFront, Secrets Manager, IAM, networking.

## Capabilities

### New Capabilities
- `picture-service-photo-storage`: PictureService's requirement to durably persist photos and their sidecar metadata independent of any single compute instance, with each photo identified by a stable generated ID rather than its original filename.
- `web-bff-api`: the stateless HTTP/JSON API surface the Web BFF exposes to the WASM client — no server-side session state is kept between requests.
- `web-photo-upload`: the browser-to-storage upload flow (client obtains a short-lived pre-authorized upload URL from the BFF and uploads the photo directly to storage), including the file-size and file-type constraints already enforced today.

### Modified Capabilities
(none — this is the first OpenSpec change in this repo, so there are no existing spec files to modify)

## Impact

- **NinjagoScanner.PictureService**: `SidecarStore`, `SidecarCache`, and the photo-write path are rewritten against S3 + DynamoDB instead of the local filesystem. Gemini analysis logic itself is unaffected.
- **NinjagoScanner.Web**: replaced by two new projects (`NinjagoScanner.Web.Client`, `NinjagoScanner.Web.Bff`). `CardCatalogService.cs` (currently a direct in-process gRPC caller using `IBrowserFile`) splits: its gRPC-calling logic moves into the BFF; the WASM client gets a new `HttpClient`-based data layer. All existing pages (`Collection.razor`, `Gallery.razor`, `Review.razor`, `Table.razor`, `Upload.razor`, etc.) are re-pointed at the new client-side data layer; their own requirements/behavior are unchanged and out of scope for this change.
- **NinjagoScanner.CatalogService**: no functional changes; redeployed onto the same Fargate/ECS setup as PictureService.
- **New AWS infrastructure**: ECS/Fargate + ALB + Service Connect (CatalogService, PictureService), Lambda + API Gateway (Web BFF), S3 + CloudFront (WASM static assets), S3 + DynamoDB (photo storage), Secrets Manager (Gemini API key), Terraform for all of it.
- **CI/CD**: new GitHub Actions workflows (build/test gate, four path-filtered deploy workflows, Terraform plan/apply).
- **One-time migration script**: not a lasting capability of the system — a disposable tool to copy existing local `cardFotos/` data into S3 + DynamoDB.
- This is cross-cutting: it spans all three existing projects plus new infrastructure that doesn't belong to any one of them.
