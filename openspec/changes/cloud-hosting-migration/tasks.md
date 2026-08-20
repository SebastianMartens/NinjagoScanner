## 1. Terraform foundations

- [x] 1.1 Set up Terraform project structure and remote state backend (S3 + DynamoDB lock table)
- [x] 1.2 Create the GitHub OIDC identity provider and IAM role(s) GitHub Actions will assume (no static AWS keys)
- [x] 1.3 Create the VPC, subnets, and networking shared by Fargate services and the BFF Lambda

## 2. Storage infrastructure

- [x] 2.1 Create the S3 bucket for photo storage (versioning/lifecycle as appropriate)
- [x] 2.2 Create the DynamoDB table for sidecar records (partition key = generated photo ID; add GSIs for the app's actual query needs, e.g. by status, by series/card)
- [x] 2.3 Create the Secrets Manager entry for the Gemini API key

## 3. PictureService: storage backend migration

- [x] 3.1 Introduce a generated photo ID (assigned at upload time) as the storage key, replacing filename-based identity
- [x] 3.2 Rewrite photo writes/reads (`SidecarStore`, photo file I/O) against S3 instead of local disk
- [x] 3.3 Rewrite sidecar record writes/reads (`SidecarCache`, `SidecarStore`) against DynamoDB instead of JSON files
- [x] 3.4 Update `MigrateSidecars` / any legacy-format handling to operate against the new backend
- [x] 3.5 Update PictureService tests (`NinjagoScanner.PictureService.Tests`) to exercise the S3/DynamoDB-backed implementation

## 4. One-time data migration script

- [x] 4.1 Write a script that copies each local photo in `cardFotos/` into S3 under a newly generated photo ID
- [x] 4.2 Write the corresponding sidecar record into DynamoDB for each migrated photo, preserving existing metadata (analysis status, review status, card match, etc.)
- [x] 4.3 Verify the script never deletes or modifies local files (copy-only) — verified via a dry run against the real `cardFotos/` (13,011 files, 6,833 images): zero writes, zero errors
- [x] 4.4 Run the script against the existing ~1.6GB/~13k-file `cardFotos/` and spot-check migrated records against originals — run manually against real S3/DynamoDB infrastructure

## 5. CatalogService and PictureService on Fargate

- [x] 5.1 Write Dockerfiles for CatalogService and PictureService
- [x] 5.2 Define ECS task definitions and services for both, with IAM roles scoped to their actual needs (PictureService: S3 + DynamoDB + Secrets Manager access)
- [x] 5.3 Configure ECS Service Connect for internal gRPC service discovery between them
- [x] 5.4 Provision the ALB (ACM-issued TLS cert) in front of whichever of these needs public reachability (if any) — confirm neither actually needs to be public, since only the BFF calls them — confirmed neither needs public reachability; no public ALB was provisioned. Used ECS Service Connect for the PictureService→CatalogService hop plus a private internal Network Load Balancer for the BFF Lambda's path, since Service Connect's Envoy-mesh DNS only works between ECS tasks, not from a Lambda function (see `infra/README.md`)
- [ ] 5.5 Deploy both services and verify they reach each other over Service Connect — infra is deployed for real (ECS cluster + both services exist in AWS account 612436161060, eu-central-1). CatalogService's image has now been pushed to ECR ( `catalog_service_image_tag` no longer needs to default to `latest`); PictureService's image is still missing, so its task is still stuck at 0/1 running — **blocked** on pushing PictureService's image (task 10.3 or a manual `docker build`/push) before Service Connect reachability between the two can actually be verified

## 6. Web split: project scaffolding

- [x] 6.1 Create `NinjagoScanner.Web.Client` (Blazor WebAssembly project)
- [x] 6.2 Create `NinjagoScanner.Web.Bff` (stateless HTTP API project, deployable to Lambda)
- [x] 6.3 Move existing `.razor` pages and components into `Web.Client`
- [x] 6.4 Retire the old `NinjagoScanner.Web` project once both new projects are in place

## 7. BFF implementation

- [x] 7.1 Port the gRPC-calling logic out of `CardCatalogService.cs` into the BFF, exposed as HTTP/JSON endpoints
- [x] 7.2 Implement BFF endpoints for series/card lookup (proxying CatalogService)
- [x] 7.3 Implement BFF endpoints for card list/sidecar read and update operations (proxying PictureService)
- [x] 7.4 Implement the pre-authorized upload URL endpoint (issues a short-lived S3 upload URL after validating file type and size)
- [x] 7.5 Implement the upload-confirmation endpoint that triggers PictureService's Gemini analysis once the client reports a completed direct upload
- [x] 7.6 Configure the BFF's gRPC clients to reach CatalogService/PictureService via the VPC's Service Connect namespace

## 8. WASM client implementation

- [x] 8.1 Replace `CardCatalogService`'s in-process gRPC calls with an `HttpClient`-based client calling the BFF
- [x] 8.2 Rewire `Collection.razor`, `Gallery.razor`, `Review.razor`, `Table.razor`, and the overview/about pages to the new client-side data layer
- [x] 8.3 Rework `Upload.razor`'s upload flow: request a pre-authorized URL from the BFF, `PUT` the photo directly to S3, then call the upload-confirmation endpoint
- [x] 8.4 Audit existing pages for any reliance on Blazor Server's automatic push-driven UI updates; replace with client-side polling against the BFF where needed (resolves design.md's open question — default is polling) — audit found no SignalR/push usage anywhere in the old Web project; every page was already plain request/response, so no polling code was needed
- [x] 8.5 Update/port relevant tests from `NinjagoScanner.Web.Tests` to cover the new client + BFF split

## 9. Web hosting infrastructure

- [x] 9.1 Define the Lambda function and API Gateway (HTTP API) for the BFF, attached to the shared VPC
- [x] 9.2 Create the S3 bucket + CloudFront distribution for the WASM client's static assets
- [x] 9.3 Wire CloudFront routing so the WASM client and the BFF's API Gateway endpoint are reachable from one public entry point — one CloudFront distribution, `/api/*` routed to the BFF's API Gateway, everything else to the S3-hosted WASM client, with a CloudFront Function (attached only to the default/static behavior, so BFF error responses are never rewritten) handling the SPA client-side-routing fallback to `index.html`
- [ ] 9.4 Measure actual cold-start latency for the VPC-attached BFF Lambda against current AWS behavior; add provisioned concurrency only if it proves necessary (resolves design.md's open question) — **blocked**: needs a real deployment to measure against. `provisioned_concurrency` is wired as a variable defaulted to 0/off, documented in `infra/README.md` as a follow-up decision once real numbers exist

## 10. CI/CD

- [x] 10.1 GitHub Actions workflow: build + `dotnet test NinjagoScanner.slnx` on pull requests (gate)
- [x] 10.2 GitHub Actions workflow: path-filtered build/push/deploy for CatalogService (image → ECR → `ecs update-service`)
- [x] 10.3 GitHub Actions workflow: path-filtered build/push/deploy for PictureService (image → ECR → `ecs update-service`)
- [x] 10.4 GitHub Actions workflow: path-filtered build/deploy for the BFF (package → Lambda `update-function-code`)
- [x] 10.5 GitHub Actions workflow: path-filtered build/deploy for the WASM client (build → S3 sync → CloudFront invalidation)
- [x] 10.6 GitHub Actions workflow: Terraform `plan` on PR, `apply` on merge, for the `infra/` directory

## 11. End-to-end verification and cutover

- [ ] 11.1 Verify CatalogService and PictureService are healthy and reachable from the BFF in the deployed environment — **blocked**: needs a real deployment
- [ ] 11.2 Verify the WASM client can list series/cards, view collection/gallery/table/review pages, and complete a full upload → analysis → review flow against the deployed BFF — **blocked**: needs a real deployment
- [ ] 11.3 Confirm the local `cardFotos/` directory remains untouched and the deployed app no longer reads from it — **blocked**: needs a real deployment (the app-side change is done — nothing in the codebase reads `cardFotos/` anymore — but confirming this in a *deployed* environment needs one to exist)
- [ ] 11.4 Cut over the public entry point (DNS) to the new CloudFront distribution — **blocked**: needs a real deployment
