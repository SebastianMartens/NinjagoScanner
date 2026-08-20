## Context

Today all three services run as local processes on one machine, coupled through a shared local directory (`cardFotos/`, currently ~1.6GB / ~13k files) that both PictureService and Web read/write directly, and through plaintext HTTP/2 gRPC (`http://`, no TLS) between them. Web runs as Blazor Server (Interactive Server render mode), which holds a persistent SignalR-backed "circuit" (server-side render state) per browser tab for the session's lifetime — including file uploads, which stream through that same circuit via `IBrowserFile`. No containers or cloud config exist yet. See proposal.md for why this needs to change.

## Goals / Non-Goals

**Goals:**
- Fully managed AWS hosting requiring minimal ongoing operational effort (no OS patching, no manually-sized always-on database).
- CatalogService and PictureService remain always-on gRPC services, unchanged in role, moved onto Fargate.
- Web's persistent-session dependency is removed so it can run on Lambda: split into a Blazor WASM static client and a stateless HTTP/JSON BFF.
- Photo storage decoupled from any single compute instance's disk.
- CI/CD that mirrors the services' existing independent-runnability: each project builds, tests, and deploys independently.
- All infrastructure reproducible from code (Terraform).

**Non-Goals:**
- Moving CatalogService or PictureService to Lambda/serverless — explicitly staying on Fargate.
- Changing any existing page's user-facing requirements or behavior (Collection, Gallery, Review, Table, Overview) — only their hosting and rendering model changes.
- Building real-time/push UI updates (e.g. a WebSocket API) as part of this change — see Decisions.

## Decisions

**Compute topology**: ECS Fargate for CatalogService and PictureService (behind one ALB with ACM TLS, ECS Service Connect for internal service-to-service gRPC); Lambda + API Gateway for the Web BFF; S3 + CloudFront for the Web WASM client's static assets.
Alternative considered: a single-VM lift-and-shift (Docker Compose on one EC2 instance). Rejected — this is a learning/portfolio-driven migration, and a single box would sidestep the distributed-AWS experience that's the point of the exercise.
Alternative considered: all three services on Lambda. Rejected for CatalogService/PictureService per explicit decision to keep them as always-on Fargate services; also API Gateway + Lambda has no native gRPC support, which those two services' contracts depend on.

**Storage backend**: S3 for photo bytes, DynamoDB for sidecar records, replacing the local filesystem entirely.
Alternative considered: EFS mounted into the Fargate tasks (keeps the current file-based code unchanged). Rejected — this relocates the shared-disk coupling instead of removing it, and photos-on-EFS is an unusual pattern; it also wouldn't work for Web once Web moves off Fargate, since Lambda's EFS support is a much heavier integration than S3 access.
Alternative considered: RDS/Aurora (relational) instead of DynamoDB for sidecar records. Rejected in favor of DynamoDB — the sidecar record is a flat per-photo document with no relational structure today, and DynamoDB needs no instance sizing, patching, or connection-pool management, matching the minimal-ops goal. Trade-off accepted: less transferable SQL skill, and less flexible ad-hoc querying than a real `WHERE` clause.

**Photo identity**: a generated identifier (assigned at upload time) becomes the S3 key and DynamoDB partition key, replacing the original filename as the identity. The original filename is retained only as metadata (`SourceFileName`).
Rationale: today's filenames already need manual collision-avoidance suffixing (e.g. `..._1.jpg`); a generated ID removes that whole class of problem and is required anyway once storage is ID-keyed rather than path-keyed.

**Web split — Blazor WASM client + stateless Lambda BFF**: replaces Blazor Server.
Rationale: Blazor Server's circuit is server-held state that persists across a whole browser session — structurally incompatible with Lambda's per-invocation, no-guaranteed-affinity execution model. Blazor WASM moves all interactive state into the browser, leaving the server side (the BFF) genuinely stateless per request, which is exactly what Lambda is built for.
Alternative considered: keep Web as Blazor Server, host it on Fargate like the other two services (this would have been the simpler, lower-risk choice). Explicitly not chosen — cost/scale-to-zero characteristics of Lambda for a personal-scale app, and the portfolio value of doing the harder split, were the deciding factors from earlier discussion.

**Upload flow — direct-to-S3 from the browser**: the BFF issues a short-lived pre-authorized upload URL; the browser uploads photo bytes straight to S3, never through the BFF/API Gateway.
Rationale: avoids Lambda's synchronous-invocation payload ceiling and API Gateway's payload limit, both well under the app's existing 15MB max upload size; also keeps large binary transfer off Lambda entirely.

**Networking**: the BFF Lambda function attaches to the same VPC as the Fargate services and reaches them via ECS Service Connect's internal DNS namespace, so gRPC traffic between the BFF and the internal services never crosses the public internet.

**CI/CD**: GitHub Actions, with per-project path-filtered workflows (a change under `NinjagoScanner.Web.Client/**` only rebuilds/redeploys the WASM client, etc.) mirroring the services' existing independent-runnability. AWS authentication via GitHub's OIDC identity provider and an IAM role trust policy — no long-lived AWS access keys stored as repo secrets.

**Infrastructure as code — Terraform over AWS CDK**: chosen for broader industry adoption and transferability, despite CDK-in-C# being an appealing same-language option for a .NET-only team. This decision doesn't constrain anything else in the design and can be revisited independently later.

**Migration — one-time copy, not move**: a disposable script copies existing local `cardFotos/` data into S3 + DynamoDB once, without deleting or altering the local files. No dual-read fallback is needed in PictureService, since this is a clean one-time cutover rather than a gradual rollout — the running app simply never reads local disk again after cutover.

**Live UI updates — polling, not push**: Blazor Server's automatic UI push (via the SignalR circuit) has no equivalent once Web is stateless. Any status/progress UI (e.g. "scan in progress") will use client-side polling against the BFF rather than a push mechanism (e.g. WebSocket API Gateway). This keeps the BFF's stateless design intact and avoids adding a second, more complex networking pattern for a personal-scale app. See Open Questions for verifying this is sufficient.

## Risks / Trade-offs

- [Risk] VPC-attached Lambda could add cold-start latency to BFF requests → [Mitigation] Validate current cold-start behavior against AWS's present-day numbers before relying on it; add provisioned concurrency only if real latency proves unacceptable.
- [Risk] DynamoDB's query model is less flexible than SQL if sidecar querying needs grow more relational later → [Mitigation] Design DynamoDB access patterns (and GSIs) around the app's actual query needs (list by status, by series/card) up front; revisit a relational store only if a genuine relational need emerges.
- [Risk] Splitting Web into two independently-deployable projects (WASM client, BFF) means their contract can drift if deployed out of sync → [Mitigation] Version the BFF's API surface explicitly; treat client/BFF as a logically paired release even though their pipelines are independent.
- [Risk] Some existing page may depend on Blazor Server's push-driven UI updates in a way polling doesn't adequately replace → [Mitigation] Audit existing pages during implementation (see Open Questions); default remains polling.
- [Risk] Code, tooling, or stored data that currently treats the original filename as a stable key breaks once identity moves to a generated ID → [Mitigation] Audit call sites during implementation.

## Migration Plan

1. Provision storage infrastructure first (S3 bucket, DynamoDB table) via Terraform, before touching PictureService.
2. Run the one-time migration script against the existing local `cardFotos/` (copy only, originals untouched).
3. Deploy PictureService (Fargate) against S3 + DynamoDB; verify it serves the migrated data correctly.
4. Deploy CatalogService (Fargate) — no data dependency, can happen independently of the above.
5. Build and deploy the BFF (Lambda + API Gateway) once CatalogService/PictureService are reachable via Service Connect in the target VPC.
6. Build and deploy the WASM client (S3 + CloudFront), pointed at the BFF's API Gateway endpoint.
7. Verify the full path end-to-end, then cut over the public entry point (DNS/CloudFront) to the new stack.

**Rollback**: the current local-process setup keeps running untouched throughout, since the migration only copies data and doesn't alter local files or the existing code paths until each service is redeployed. Rolling back means pointing traffic back at the local processes; no data recovery is needed because nothing local was modified.

## Open Questions

- Do any existing Web pages rely on Blazor Server's push-driven UI updates in a way plain polling won't adequately replace? Default decision is polling; verify against the actual pages during implementation.
- What do current AWS numbers say about VPC-attached Lambda cold-start latency, to confirm Service Connect networking doesn't need provisioned concurrency from day one?
