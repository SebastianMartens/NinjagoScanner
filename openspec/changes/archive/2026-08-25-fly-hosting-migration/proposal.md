## Why

`aws-compute-teardown` removes AWS compute entirely, leaving the app with nowhere to run. This change gives it a new home — Fly.io, hosting all three services as containers — and, since Fly's always-on machines remove the one constraint (Lambda statelessness) that justified splitting `NinjagoScanner.Web` into a WASM client + a stateless BFF, reverts that split too. For an app this small, going back to a single Blazor Server project is less to build, deploy, and reason about than keeping two separately-coded, separately-deployed projects and a JSON API layer between them that no longer serves its original purpose.

## What Changes

- **BREAKING**: `NinjagoScanner.Web.Client` and `NinjagoScanner.Web.Bff` are retired and replaced by a single project, `NinjagoScanner.Web`, running Blazor Server (Interactive Server render mode) — no WASM bundle, no separate JSON API surface. Pages call CatalogService/PictureService over gRPC directly from the server, as the app did before `cloud-hosting-migration`. `NinjagoScanner.Web.Shared` is retired too — nothing needs a shared client/server contract once there's no client project.
- **BREAKING**: Photo upload reverts from browser-direct-to-S3 (a presigned URL issued by the BFF) to streaming through the app server: the browser sends bytes to `NinjagoScanner.Web` over the Blazor Server circuit, which streams them to PictureService over a revived gRPC `UploadPhoto` client-streaming RPC. PictureService assigns the generated photo ID and writes to S3 itself, the same as it does for every other S3 write today — no change to PictureService's ownership of storage, only to how bytes arrive.
- **BREAKING**: Photo *display* (gallery/table/review pages) also moves off directly-browser-to-S3: `NinjagoScanner.Web` asks PictureService for a short-lived presigned GET URL per photo (PictureService already holds AWS credentials for storage; nothing new needed here), and the browser fetches image bytes straight from S3 using that URL — keeping bulk image traffic off the app server while keeping AWS credentials confined to one service.
- New Dockerfile for `NinjagoScanner.Web`, alongside the existing `NinjagoScanner.CatalogService`/`NinjagoScanner.PictureService` Dockerfiles.
- All three services become Fly.io apps in one Fly organization/region, connected over Fly's private network (6PN / `*.internal` DNS); only `NinjagoScanner.Web` gets a public Fly IP.
- PictureService's AWS access moves from ECS task-role STS credentials to a static IAM user's access key/secret, injected as a Fly secret. This is a real security posture downgrade (long-lived credentials vs. auto-rotated temporary ones), accepted as a pragmatic trade-off for a personal-scale app — see design.md for the scoping applied to limit the blast radius. No other service needs AWS credentials.
- New Terraform: a small IAM-user module granting PictureService exactly the S3/DynamoDB access it needs, added alongside the storage modules `aws-compute-teardown` leaves in place.
- New GitHub Actions workflows deploying to Fly via `flyctl deploy` (path-filtered per service), replacing the AWS-compute deploy workflows `aws-compute-teardown` deletes. Fly API deploy tokens become GitHub Actions secrets.
- `infra/README.md` and `CLAUDE.md` updated to describe the resulting architecture (both already describe stale states today — `CLAUDE.md` still documents the pre-split single-Web-project architecture that this change actually restores, so most of that file turns out to need less editing than it looks).

## Capabilities

### New Capabilities
(none)

### Modified Capabilities
- `web-photo-upload`: the "Direct-to-storage upload" requirement (browser gets a pre-authorized URL from the BFF, uploads directly to storage) is replaced — upload now streams browser → `NinjagoScanner.Web` → PictureService via gRPC.
- `picture-service-photo-upload`: this capability's existing spec already describes a client-streaming `UploadPhoto` RPC, but that RPC doesn't exist in the current proto (`cloud-hosting-migration` removed it when upload moved to direct-to-S3, without updating this spec — a pre-existing drift this change also corrects). The revived RPC keeps the metadata-then-bytes streaming shape and file-type validation, but drops every local-filesystem requirement (sanitized file name, target directory, collision retry) in favor of S3's generated-photo-ID identity model, and folds in triggering AI analysis on completion.

### Removed Capabilities
- `web-bff-api`: retired outright. There is no separate BFF/JSON API layer once `NinjagoScanner.Web` is Blazor Server holding per-request server state directly — the capability's core requirement ("stateless request handling") is no longer even meaningful for this project.

## Impact

- **`NinjagoScanner.Web.Client`, `NinjagoScanner.Web.Bff`, `NinjagoScanner.Web.Shared`**: all three retired. Replaced by one new `NinjagoScanner.Web` project (Blazor Server), rebuilding page logic from `Web.Client`'s Razor components against server-side gRPC calls instead of the BFF's JSON HTTP client.
- **`NinjagoScanner.PictureService/Protos/picture_service.proto`**: `UploadPhoto` client-streaming RPC re-added (S3-shaped, not the old local-disk shape); a new presigned-GET-URL RPC added for photo display; `AnalyzePhoto` (which existed solely to register a browser-direct S3 upload) is removed since nothing calls it once upload streams through PictureService directly — see design.md.
- **`NinjagoScanner.PictureService/PhotoStore.cs`**: gains the write path for streamed uploads (previously only read/deleted S3 objects the BFF wrote).
- **New Dockerfile**: `NinjagoScanner.Web/Dockerfile`.
- **`infra/environments/prod/`**: one new small module (IAM user for PictureService's cross-cloud access) added on top of what `aws-compute-teardown` leaves behind (`photo_storage`, `sidecar_table`, `bootstrap`, `github_oidc`).
- **`.github/workflows/`**: new `deploy-web.yml`, `deploy-catalog-service.yml`, `deploy-picture-service.yml` (flyctl-based).
- **`infra/README.md`, `CLAUDE.md`**: updated.
- **No functional impact** to `NinjagoScanner.CatalogService`, to PictureService's Gemini analysis logic, or to any `openspec/specs/web-*` page-behavior capability (gallery/table/review/collection/overview) beyond how it obtains data and photo URLs — their observable UI behavior is unchanged.
