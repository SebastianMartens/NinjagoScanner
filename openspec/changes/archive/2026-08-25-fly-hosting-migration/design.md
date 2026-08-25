## Context

See proposal.md for motivation. Relevant current-state facts that shape the approach:

- `NinjagoScanner.Web.Bff/Program.cs` maps roughly a dozen minimal-API endpoints (`/api/series`, `/api/collection/*`, `/api/gallery`, `/api/review-groups`, `/api/photos/{id}/*`, `/api/scan`, `/api/uploads*`), almost all of which are thin wrappers around `Services/CardCatalogService.cs`'s methods, which already do the real work of calling CatalogService/PictureService over gRPC. That class is the part of the BFF worth keeping — the HTTP mapping layer around it is what's being removed.
- `NinjagoScanner.Web.Client`'s pages (`Collection.razor`, `Gallery.razor`, `Review.razor`, `Table.razor`, `Upload.razor`, etc.) fetch data via an `HttpClient` pointed at the BFF's JSON endpoints.
- `NinjagoScanner.PictureService/Protos/picture_service.proto` today has `AnalyzePhoto` (registers a photo the browser already put in S3 via presigned URL, then runs analysis) but no upload-receiving RPC — `UploadPhoto` (client-streaming) existed before `cloud-hosting-migration` and was removed when upload moved to direct-to-S3.
- `NinjagoScanner.Web.Bff/IUploadUrlIssuer.cs` (`S3UploadUrlIssuer`) is the only place in the current codebase that holds AWS credentials outside PictureService — it issues both upload (PUT) and download (GET) presigned URLs.
- Kestrel can't serve HTTP/1.1 and cleartext HTTP/2 (gRPC) on the same port — this is why `infra/modules/fargate-service`/`internal-lb` used a separate health-check port (`8082`) alongside the gRPC port (`8080`) for CatalogService/PictureService. That constraint is about Kestrel, not ECS, so it carries over to Fly regardless of hosting platform.

## Goals / Non-Goals

**Goals:**
- One Blazor Server project (`NinjagoScanner.Web`) with no separate client/API split.
- PictureService remains the only service holding AWS credentials; `NinjagoScanner.Web` never talks to AWS directly.
- Reuse `CardCatalogService.cs`'s existing gRPC-calling logic as-is where possible, rather than rewriting it — only its callers change (Razor components instead of minimal-API handlers).
- All three services deployable to Fly.io independently, over Fly's private network, with only `NinjagoScanner.Web` publicly reachable.

**Non-Goals:**
- Changing CatalogService in any way.
- Changing Gemini analysis logic, sidecar field semantics, or any page's visual behavior/output — this is a hosting and data-plumbing change, not a UX change.
- Deciding whether Fly itself should be provisioned via the unofficial community Terraform provider — resolved below (Decision 6), not left open.

## Decisions

**1. `CardCatalogService.cs` moves into `NinjagoScanner.Web` as its server-side data layer; the minimal-API mapping in `Program.cs` is deleted, not ported.**
Razor components (with `@rendermode InteractiveServer`, or set app-wide via `AddInteractiveServerRenderMode()`) inject `CardCatalogService` directly and call its methods (`GetKnownSeriesAsync`, `GetCollectionOverviewAsync`, `UpdateReviewStatusAsync`, etc.) instead of going through `HttpClient` + JSON. `Web.Client`'s Razor markup/`@code` blocks are the starting point for each page, with their data-fetching bodies rewritten from `HttpClient.GetFromJsonAsync(...)` to `await CardCatalogService.GetXAsync(...)`. This is the direct undo of the split `cloud-hosting-migration` performed — the gRPC-calling logic doesn't need to change, only what calls it.

**2. Upload: revive `UploadPhoto` as a client-streaming RPC on PictureService, replacing `AnalyzePhoto` + the presigned-URL dance.**
`NinjagoScanner.Web`'s upload page reads the browser's `IBrowserFile` and streams it (metadata message, then byte-chunk messages) to PictureService via `UploadPhoto`. PictureService validates type/size, generates a photo ID, writes the bytes to S3 (`PhotoStore` gains a write path it didn't need before — until now, PictureService only ever read/deleted S3 objects the BFF had written), creates the sidecar record, and triggers Gemini analysis before returning. This collapses what was three round trips (`POST /uploads` → PUT to S3 → `POST /uploads/{id}/confirm`) into one streamed call. `IUploadUrlIssuer`/`S3UploadUrlIssuer` and `AnalyzePhoto` are deleted — nothing calls them once this lands.

**3. Photo display: a new PictureService RPC issues a short-lived presigned GET URL per photo; the browser fetches image bytes directly from S3.**
Alternative considered: have `NinjagoScanner.Web` (or PictureService) proxy the actual image bytes to the browser, which would mean no S3-shaped URL ever reaches the client. Rejected: gallery/table pages render dozens of images at once, and adding a full byte-proxy hop for every image view is real, ongoing bandwidth cost for both services, whereas a small RPC returning a URL is cheap and lets the browser fetch straight from S3 — the same trade-off `cloud-hosting-migration` already made for downloads, just with PictureService as the URL-issuer instead of the BFF (since PictureService is now the only service with AWS credentials).

**4. PictureService's IAM: one static IAM user, access key/secret as a Fly secret, scoped to exactly what the app needs.**
New Terraform module (alongside `photo_storage`/`sidecar_table`, which `aws-compute-teardown` keeps): an `aws_iam_user` + inline/managed policy granting `s3:GetObject`/`s3:PutObject`/`s3:DeleteObject`/`s3:ListBucket` on the photo bucket's `photos/*` prefix and `dynamodb:GetItem`/`PutItem`/`DeleteItem`/`Scan` on the sidecar table — the same actions the ECS task role granted, plus `s3:PutObject`, which the ECS design deliberately withheld from PictureService because upload authority lived in the BFF (Decision 2 above moves that authority into PictureService itself, so it needs the permission now). No other service gets AWS credentials.

**5. Fly.io configuration: `fly.toml` per app, deployed via `flyctl`, not the unofficial Terraform Fly provider.**
Considered using Terraform for Fly resources too, for consistency with the rest of this repo's IaC. Rejected: the community `fly-apps/terraform-provider-fly` provider is unofficial and lags Fly's own platform; `fly.toml` + `flyctl` is Fly's own first-party config-as-code (declarative, checked into each project directory, diffable in PRs) and is what Fly's own GitHub Action (`superfly/flyctl-actions`) is built around. Terraform stays scoped to AWS resources only (storage + the new IAM user); Fly apps are provisioned with `flyctl apps create` once, then updated via `flyctl deploy --config <project>/fly.toml` on every push.

**6. Health checks carry forward the same HTTP/1.1-vs-h2c port split used on ECS.**
`catalog-service`/`picture-service` keep exposing gRPC on one port and a plain-HTTP health check on a second port, matching Kestrel's inability to serve both protocols on one listener — this is a Kestrel constraint, not an ECS one, so it applies identically on Fly. Fly's own health-check config (`[[services.tcp_checks]]`/`[[services.http_checks]]` in `fly.toml`) points at the second port. This can't be fully verified against a real Fly account from this environment; flagged in tasks.md as something to confirm during implementation rather than left as an open design question, since the fallback (keep the existing port split) costs nothing if unnecessary.

**7. Naming: the merged project is `NinjagoScanner.Web`; Fly apps are `ninjago-scanner-web`, `ninjago-scanner-catalog-service`, `ninjago-scanner-picture-service`.**
Matches `infra`'s existing `project_name` slug (`ninjago-scanner`) rather than inventing a new naming scheme.

## Risks / Trade-offs

- **[Risk] Rewriting every page's data-fetching from `HttpClient`/JSON to direct `CardCatalogService` calls is the bulk of this change's manual effort** and touches every page (`Collection`, `Gallery`, `Review`, `Table`, `Upload`, `Overview`, `About`) — more surface area for a mistake than the infra pieces. → **Mitigation**: `CardCatalogService.cs`'s method signatures barely change (same inputs/outputs, just called in-process instead of over HTTP+JSON), and existing tests in `NinjagoScanner.Web.Bff.Tests`/`NinjagoScanner.Web.Client.Tests` cover much of this logic today — port and adapt them rather than starting from scratch (see tasks.md).
- **[Risk] `PictureService`'s task role gaining `s3:PutObject`** (Decision 4) slightly widens what a compromised PictureService credential could do (write, not just read/delete) compared to today's ECS split. → **Mitigation**: this is an inherent consequence of collapsing upload authority into the same service that already holds delete authority — accepted, matches the "PictureService owns photo storage" architecture this change restores.
- **[Trade-off] Static long-lived AWS credentials (Fly secret) instead of ECS's auto-rotated STS credentials.** → No new mitigation beyond scoping the IAM user tightly (Decision 4); already accepted in the broader migration discussion, not specific to this change.
- **[Risk] Streaming a large photo over a Blazor Server circuit (SignalR) rather than a direct HTTP PUT to S3 adds load to `NinjagoScanner.Web` for every upload.** → **Mitigation**: accepted per the explicit decision to revert this flow (photo uploads are infrequent, single-user-at-a-time in practice, and the max upload size is already capped at 15 MB by `web-app-configuration`'s existing default).

## Migration Plan

1. PictureService: add `UploadPhoto` (client-streaming, S3-shaped) and a presigned-GET-URL RPC to `picture_service.proto`; implement both; remove `AnalyzePhoto`; give `PhotoStore` a write path; wire the new IAM-user credentials.
2. Terraform: add the IAM-user module; wire its output into Fly secrets (manually or via a short documented step, since Fly secrets aren't Terraform-managed here).
3. Create `NinjagoScanner.Web` (new project): port `CardCatalogService.cs` from `Web.Bff`, port pages from `Web.Client` with data-fetching rewritten per Decision 1, wire the new upload/download RPCs.
4. Add `NinjagoScanner.Web/Dockerfile`; add `fly.toml` to all three service projects.
5. Retire `NinjagoScanner.Web.Client`, `NinjagoScanner.Web.Bff`, `NinjagoScanner.Web.Shared` and their test projects (porting still-relevant tests into `NinjagoScanner.Web.Tests` first).
6. Stand up the three Fly apps (`flyctl apps create`), set secrets, first manual `flyctl deploy` for each.
7. Add the three `deploy-*.yml` GitHub Actions workflows; retire the placeholder AWS-shaped ones already deleted by `aws-compute-teardown`.
8. Update `infra/README.md` and `CLAUDE.md`.

No rollback beyond redeploying the prior AWS-hosted artifacts if `aws-compute-teardown` hasn't already destroyed them by the time this change starts — sequencing (this change assumes `aws-compute-teardown` has already landed) makes that unavailable in practice, which is consistent with the accepted downtime window between the two changes.
