# Infrastructure (Terraform)

AWS infrastructure for the `cloud-hosting-migration` OpenSpec change (see
`openspec/changes/cloud-hosting-migration/{proposal,design,tasks}.md`). This
covers tasks 1.1–2.3 (Terraform foundations + storage infrastructure), task 5
(CatalogService/PictureService on ECS Fargate), and task 9.1–9.3 (the Web
BFF's Lambda + API Gateway, and the WASM client's S3 + CloudFront
distribution, routed as one public entry point). CI/CD workflow YAML (task
10), the application code changes (tasks 3, 4, 6–8), and end-to-end
verification/cutover (task 11) are later/parallel work — see "What's not
here yet" below. Task 9.4 (measuring real cold-start latency) is also not
done from here — see "Cold start / provisioned concurrency" below for why
and what's recommended instead.

## Layout

```
infra/
  bootstrap/                 Root module: the Terraform state backend itself.
                              Applied once per AWS account, by hand, with
                              local state (see "Bootstrapping" below).
  environments/
    prod/                    Root module: everything else. Uses the S3
                              backend bootstrap created. This is the only
                              environment that exists today — a second one
                              (e.g. "dev") would be a sibling directory
                              reusing the same modules/.
  modules/
    state-backend/            S3 bucket + DynamoDB table for Terraform state
                               (used only by bootstrap/).
    github-oidc/               GitHub Actions OIDC provider + plan/deploy
                                IAM roles (task 1.2).
    networking/                 VPC, public/private subnets across 2 AZs,
                                 IGW, NAT Gateway(s), S3 + DynamoDB gateway
                                 endpoints (task 1.3).
    photo-storage/               S3 bucket for card photos: versioning,
                                  CORS (for direct browser upload), lifecycle
                                  rules (task 2.1).
    sidecar-table/                 DynamoDB table for sidecar records +
                                    GSIs (task 2.2).
    secrets/                        Secrets Manager entry for the Gemini API
                                     key — container only, no value (task 2.3).
    ecr-repo/                        One ECR repository (image scanning +
                                      lifecycle policy), instantiated once
                                      per service image (task 5.2).
    ecs-cluster/                      ECS cluster + the Cloud Map "HTTP"
                                       namespace ECS Service Connect uses for
                                       internal gRPC discovery (task 5.3).
    fargate-service/                   Generic ECS Fargate service: task
                                        definition + service, execution/task
                                        IAM roles, log group, Service Connect
                                        registration (task 5.2/5.3).
    internal-lb/                        Shared internal (private) Network
                                         Load Balancer fronting both services
                                         for the BFF Lambda — see "Task 5.4"
                                         below (task 5.4).
    bff-lambda/                          The Web BFF (NinjagoScanner.Web.Bff)
                                          on Lambda + API Gateway (HTTP API),
                                          VPC-attached to reach
                                          CatalogService/PictureService via
                                          the internal NLB above (task 9.1).
    static-site/                         S3 bucket (private, CloudFront-only
                                          via Origin Access Control) for the
                                          WASM client's static assets, plus
                                          the CloudFront distribution that
                                          also proxies the BFF's API Gateway
                                          endpoint under "/api/*" from the
                                          same public domain (task 9.2/9.3).
```

`environments/prod/main.tf` composes `networking`, `photo-storage`,
`sidecar-table`, `secrets`, `github-oidc`, `ecr-repo` (×2), `ecs-cluster`,
`internal-lb`, `fargate-service` (×2, for CatalogService and PictureService),
`bff-lambda`, and `static-site` into one deployable stack.
`environments/prod/iam-policies.tf` builds the IAM policy documents attached
to the two GitHub Actions roles, since only the root module knows every
resource ARN those policies need to reference.
`environments/prod/ecs-task-policies.tf` builds PictureService's ECS *task*
role policy (its application code's own S3/DynamoDB access — see that
file's header comment for exactly which C# call sites justify each
statement).

Every resource is tagged `Project = "ninjago-scanner"`, `ManagedBy =
"terraform"`, `Environment = "prod"` (see `local.common_tags` in
`environments/prod/main.tf`, applied via the AWS provider's `default_tags`).

## State & locking

Terraform state lives in S3 (bucket created by `bootstrap/`), with a
DynamoDB table providing locking so two `terraform apply` runs (e.g. a human
and a CI job) can't race each other. `environments/prod` never manages the
bucket/table it stores its own state in — that would be circular — it only
consumes them as a backend.

## Bootstrapping (first-time setup, once per AWS account)

1. **Create the state backend.** This step has no remote backend yet — it
   uses local state:
   ```powershell
   cd infra/bootstrap
   terraform init
   terraform apply
   terraform output state_backend_config
   ```
   Keep `infra/bootstrap/terraform.tfstate` somewhere safe afterwards (it's
   gitignored, and only exists on the machine that ran this). It changes
   rarely — only re-run this if the state backend itself needs to change.

2. **Point `environments/prod` at that backend:**
   ```powershell
   cd infra/environments/prod
   cp backend.hcl.example backend.hcl        # then fill in the values from step 1
   cp terraform.tfvars.example terraform.tfvars   # same — state_bucket_arn / state_lock_table_arn
   terraform init -backend-config=backend.hcl
   ```

3. **First apply, with your own AWS credentials** (`aws configure` / SSO /
   whatever you already use locally — not GitHub Actions yet):
   ```powershell
   terraform plan
   terraform apply
   ```
   This is the one apply that *must* be run by a human. The GitHub Actions
   deploy role created by this apply is granted permission to manage its own
   IAM role/policy resources (see the comment block at the top of
   `iam-policies.tf`) — a normal but self-referential pattern — so it can't
   be the thing that creates itself the first time.

4. **Set the Gemini secret's real value** (never via Terraform/committed):
   ```powershell
   aws secretsmanager put-secret-value `
     --secret-id (terraform output -raw gemini_secret_name) `
     --secret-string '{"ApiKey":"...","Model":"gemini-2.5-flash"}'
   ```
   Note the JSON keys are `ApiKey`/`Model`, not `Gemini:ApiKey`/`Gemini:Model`
   — see "PictureService's Gemini secret wiring" below for why.

5. From here on, GitHub Actions workflows (task 10.6, not built yet) can run
   `terraform plan`/`apply` against this same config using the `deploy` role
   via OIDC — no AWS keys stored in GitHub.

## Everyday usage (once bootstrapped)

```powershell
cd infra/environments/prod
terraform plan
terraform apply
```

## Design decisions worth knowing about

- **Region: `eu-central-1`** (Frankfurt), set as a variable
  (`var.aws_region`, default `eu-central-1`) rather than hardcoded —
  closest AWS region to the app's German-language user base (the app's
  sidecar records default to `de` when unset — see CLAUDE.md).
- **Two GitHub Actions IAM roles, not one**, both federated through a single
  OIDC provider (`modules/github-oidc`):
  - `plan`: assumable from pull-request-triggered runs *and* main-branch
    runs; read-only permissions. Used for `terraform plan` and CI checks.
  - `deploy`: assumable **only** from workflow runs triggered by a push to
    `main`; write permissions. Used for `terraform apply` and the
    project-specific deploy workflows tasks 10.2–10.5 will add.
  A pull request — including from a fork — can therefore never assume a
  role capable of changing real infrastructure. This directly matches
  proposal.md's "deploys per-project on push to main."
- **IAM policy scope**: both roles' policies cover exactly the resources
  this Terraform config manages today (networking, S3 photo bucket,
  DynamoDB sidecar table, the Gemini secret, the OIDC provider/roles
  themselves, ECR/ECS Fargate/Service Connect/the internal NLB (task 5),
  and the BFF Lambda/API Gateway/the WASM client's S3 bucket/CloudFront
  distribution (task 9)) plus read/write access to the Terraform state
  backend. ACM is the one thing still not granted — unused until task 11.4
  supplies a real custom domain — see the comment block at the top of
  `environments/prod/iam-policies.tf`.
- **EC2 networking permissions use `resources = "*"`**: unlike S3 buckets or
  DynamoDB tables (whose names — and therefore ARNs — are chosen up front),
  most VPC-family resource IDs (`vpc-...`, `subnet-...`, `rtb-...`) are
  AWS-assigned only after creation, so IAM can't scope a statement to "the
  VPC this stack will create" before it exists. The networking statements
  are scoped by action allow-list instead of by resource ARN — a documented
  trade-off, not an oversight.
- **VPC layout**: one `/16`, public + private `/24` subnets across 2 AZs
  (`var.az_count`, minimum 2 — enforced by a validation block), one Internet
  Gateway, and NAT Gateway(s) for private-subnet egress. Public subnets hold
  only the NAT Gateways — there is no public ALB anywhere in this stack (see
  "Task 5.4" below); private subnets hold the Fargate tasks, the internal
  NLB fronting them, and (later) the BFF Lambda's ENIs from task 9. S3 and
  DynamoDB gateway VPC endpoints are included so PictureService's traffic to
  the photo bucket and sidecar table never needs to leave the VPC via NAT.
- **Single NAT Gateway by default** (`var.single_nat_gateway = true`): one
  NAT Gateway in the first AZ, shared by all private subnets, rather than
  one per AZ. Cheaper for a personal-scale project; the trade-off is that an
  AZ-level outage in that specific AZ takes down private-subnet egress for
  every AZ, not just its own. Flip the variable to `false` for full per-AZ
  redundancy if that trade-off stops being acceptable.
- **DynamoDB sidecar table**: `PAY_PER_REQUEST` billing (no capacity
  planning), point-in-time recovery on, deletion protection on by default.
  Partition key `PhotoId` (a generated ID, not the original filename — see
  design.md's "Photo identity" decision), with three GSIs matching the
  app's three real query patterns today: by review status, by analysis
  status, and by series+card-number (how "Owned Copies" is computed — see
  `openspec/GLOSSARY.md`). See the comment block at the top of
  `modules/sidecar-table/main.tf` for the full reasoning, including a naming
  note: the table's `SeriesName` attribute corresponds to what
  PictureService's current C# code calls `SetName`.
- **S3 photo bucket**: versioning + SSE-S3 encryption + all-public-access
  blocked, with a CORS rule (currently `allowed_origins = ["*"]`) so the
  browser can `PUT` directly to S3 using a presigned URL (task 7.4's job to
  issue). Task 9.2/9.3 created the WASM client's actual CloudFront domain
  this was meant to be tightened to, but wiring it automatically isn't
  possible without a real dependency cycle — see
  `photo_bucket_cors_origins`' variable description for why, and for the
  manual "apply once, tighten by hand, re-apply" step this leaves in its
  place. Lifecycle rules abort stale multipart uploads
  after 7 days and expire noncurrent object versions after 30 days —
  bounding storage growth from the versioning safety net without touching
  the current (actively viewed) version of any photo.
- **Secrets Manager secret has no value in Terraform.** Only the secret
  *container* is declared; the actual Gemini API key is set once, out of
  band, via `aws secretsmanager put-secret-value` (see "Bootstrapping" step
  4) — never committed, never in state as plaintext.
- **PictureService's Gemini secret wiring.** The ECS task definition injects
  `Gemini:ApiKey`/`Gemini:Model` as container environment variables
  `Gemini__ApiKey`/`Gemini__Model` (the double-underscore convention .NET's
  configuration system maps to colon-separated keys), sourced from specific
  JSON keys inside the one Secrets Manager secret via ECS's
  `<secret-arn>:<json-key>::` `valueFrom` syntax. Those JSON keys are named
  `ApiKey`/`Model` — **not** `Gemini:ApiKey`/`Gemini:Model` — deliberately:
  the json-key segment of that colon-delimited reference string is itself
  parsed on colons, so a JSON key that itself contains a colon is exactly
  the kind of thing that syntax can't safely represent. Avoiding colons in
  the secret's own JSON keys sidesteps the ambiguity entirely; the resulting
  container env var name (`Gemini__ApiKey`) is what actually needs to match
  what ScannerConfig.cs reads, not the JSON key inside the secret. See
  `modules/fargate-service`'s `secrets` variable and
  `environments/prod/main.tf`'s `module.picture_service` block.
- **Task 5.4 — why there's an internal NLB and not a public ALB.** Task 5.4
  asks us to confirm neither CatalogService nor PictureService needs public
  reachability, since only the Web BFF and each other call them — confirmed:
  no public ALB exists anywhere in this stack, both services are reachable
  only from inside the VPC. The less obvious part: design.md's Networking
  decision assumed the BFF Lambda would reach both services "via ECS Service
  Connect's internal DNS namespace," but that doesn't actually work — Service
  Connect's internal names are resolved by the Envoy-based proxy sidecar ECS
  injects into each *participating ECS task*'s network namespace; a Lambda
  function, even one attached to the same VPC/subnets, is not an ECS task,
  never receives that sidecar, and has no other way to join the Service
  Connect mesh. So Service Connect (`modules/ecs-cluster`) is used only for
  the one ECS-to-ECS hop that actually needs it — PictureService calling
  CatalogService (task 5.3) — and a separate, internal-only Network Load
  Balancer (`modules/internal-lb`) is what the BFF Lambda will actually call.
  NLB rather than ALB: both services speak plaintext HTTP/2 gRPC with no TLS
  today (matching the existing same-machine setup), and an ALB only supports
  HTTP/2 on an HTTPS listener — provisioning/rotating an ACM cert for a hop
  that never leaves the VPC isn't worth it, whereas an NLB is a pure L4 TCP
  passthrough that carries cleartext HTTP/2 transparently. One NLB is shared
  by both services (two listeners, two target groups) rather than one each,
  matching this stack's existing cost-consciousness (see single NAT Gateway
  above). See `modules/internal-lb/main.tf`'s header comment for the full
  writeup.
- **PictureService's task role, verified against source, not design.md's
  prose.** `s3:GetObject`/`s3:DeleteObject`/`s3:ListBucket` on the photo
  bucket's `photos/*` prefix, and `dynamodb:GetItem`/`PutItem`/`DeleteItem`/
  `Scan` on the sidecar table — nothing more. No `s3:PutObject` (uploads go
  browser-to-S3 directly per design.md's upload-flow decision;
  `PhotoStore.cs` never calls `PutObjectAsync`), and no GSI-scoped
  permissions (`SidecarTable.cs`'s `ListAllAsync` does a full table `Scan`,
  never a `Query` against an index, despite the table having three GSIs —
  see `environments/prod/ecs-task-policies.tf`'s header comment). No
  Secrets Manager permission either: the Gemini API key/model are resolved
  into environment variables by the ECS *execution* role before the
  container starts (see the bullet above); the application code only ever
  reads them back out of `IConfiguration`, it never calls the Secrets
  Manager SDK directly. CatalogService gets no task role at all — it only
  reads local `cardInfos/*.json` baked into its own image.
- **Self-managing IAM role.** The `deploy` role's policy grants it rights to
  manage its own role/policy and the OIDC provider (scoped tightly to those
  exact resource names — see `iam-policies.tf`'s `SelfManagedIamRoles` /
  `SelfManagedOidcProvider` statements — never `iam:*`). This lets future
  Terraform changes to the CI role itself go through the same
  plan-on-PR/apply-on-main pipeline as everything else, at the cost of the
  bootstrapping wrinkle described above (first apply must be run by a
  human).
- **Task 9.1 — BFF Lambda runtime: `provided.al2023` (self-contained,
  arm64), not a managed `dotnet` runtime.** `NinjagoScanner.Web.Bff` targets
  net10.0; this environment has no AWS CLI/credentials to check what managed
  .NET runtime versions Lambda actually supports today in `eu-central-1`,
  and AWS's managed .NET runtimes have historically lagged new .NET releases
  by some months. Rather than gamble on a `dotnet10` managed runtime
  existing, `modules/bff-lambda` targets the custom-runtime family
  (`provided.al2023`) with a self-contained deployment package — the
  AWS-documented pattern for ASP.NET Core Minimal APIs on Lambda regardless
  of managed-runtime availability, and exactly what
  `Amazon.Lambda.AspNetCoreServer.Hosting`'s `AddAWSLambdaHosting` already
  supports without any Program.cs changes. Task 10.4's deploy workflow is
  expected to `dotnet publish -r linux-arm64 --self-contained true` and
  package the output as `bootstrap` at the zip root. `arm64` (Graviton) is
  used for both the Lambda function and the publish target — cheaper than
  x86_64 for the same memory/duration, matching this stack's existing
  cost-consciousness (single NAT Gateway, Container Insights off). If AWS
  ships a `dotnet10` managed runtime before task 10.4 is written, switching
  `runtime` in `modules/bff-lambda/main.tf` is a one-line change.
- **Task 9.1 — Lambda deployment package: a placeholder Terraform manages
  once, CI owns after that.** Unlike ECS (which happily references an image
  tag that doesn't exist yet in ECR and just fails to start tasks — see
  `catalog_service_image_tag`'s variable description), `aws_lambda_function`
  requires a real zip to exist at creation time. `modules/bff-lambda`
  generates a trivial placeholder via the `archive_file` data source (the
  one new Terraform provider this stack needed — `hashicorp/archive`, added
  to `versions.tf`) and marks `filename`/`source_code_hash` as
  lifecycle-ignored, so every `terraform apply` after the first leaves
  whatever task 10.4's CI workflow last deployed via
  `aws lambda update-function-code` alone.
- **Task 9.1 — one IAM role for the BFF Lambda, not an execution/task
  split.** `modules/fargate-service` splits an *execution* role (ECS agent:
  image pull, logs, secrets) from a *task* role (application code via the
  AWS SDK) because ECS hands two different principals two different
  credentials inside one running task. Lambda has no equivalent split —
  there's exactly one execution role, and it's also the identity the AWS
  SDK inside the function code runs as — so `modules/bff-lambda` creates one
  role covering both the platform's needs (CloudWatch Logs, ENI
  create/describe/delete for VPC attachment — the hand-written equivalent of
  the AWS-managed `AWSLambdaVPCAccessExecutionRole` policy) and the
  application's own needs. Verified against source, not assumed: the only
  AWS call `NinjagoScanner.Web.Bff` makes directly is
  `S3UploadUrlIssuer`'s `GetPreSignedURLAsync`, for both the upload (PUT)
  and download (GET) presigned URLs — so the role gets `s3:PutObject` and
  `s3:GetObject` on the photo bucket's `photos/*` prefix, nothing else.
  Presigning itself is a local SigV4 computation with no network call, but
  the *use* of the resulting URL (the browser's actual PUT/GET against S3)
  is authorized against this role's permissions at request time — a
  presigned URL only carries as much authority as its signer has. This is
  deliberately the mirror image of PictureService's own task role
  (`ecs-task-policies.tf`), which has no `s3:PutObject` at all: the BFF is
  the one place upload authority is minted, PictureService's own code never
  writes photo bytes itself.
- **Task 9.1 — the BFF Lambda's security group is scoped tightly in one
  direction, not two.** Its egress only reaches `modules/internal-lb`'s
  security group on the two listener ports (8080/8081) and the S3 gateway
  VPC endpoint's prefix list on 443 — nothing else, no `0.0.0.0/0` rule.
  The reverse direction (scoping the internal NLB's *ingress* to this
  specific security group, rather than the whole VPC CIDR it uses today)
  was considered and rejected: it would require `modules/internal-lb` to
  take the Lambda's security group ID as an input while `modules/bff-lambda`
  simultaneously takes the NLB's DNS name/security group ID as inputs —
  wiring both directions is a real Terraform module dependency cycle, not
  just an ordering inconvenience. Since the NLB is already unreachable from
  outside the VPC (`internal = true`, no public route) and only Fargate
  tasks and this Lambda ever run in the private subnets it accepts traffic
  from, VPC-wide CIDR ingress there remains an accepted trade-off — see
  `modules/internal-lb`'s `ingress_cidr` variable description for the full
  writeup.
- **Task 9.2/9.3 — one CloudFront distribution, two origins, so the browser
  never needs CORS.** `modules/static-site` serves the WASM client's static
  assets (S3, via Origin Access Control — the modern replacement for the
  older Origin Access Identity, never a public bucket policy) as its default
  cache behavior, and proxies the BFF's API Gateway endpoint under the path
  pattern `/api/*` as a second, uncached origin on the *same* distribution/
  domain. Because both are served from one origin as far as the browser is
  concerned, a WASM client request to `/api/series` is same-origin — no
  preflight, no `Access-Control-Allow-Origin` check, nothing to configure.
  `NinjagoScanner.Web.Client`'s `BffBaseAddress` (`wwwroot/appsettings.json`)
  already relies on exactly this: it falls back to
  `WebAssemblyHostBuilder`'s own `HostEnvironment.BaseAddress` ("call
  whatever origin I was served from") whenever no explicit value is
  configured, so production doesn't need to set it once this distribution
  exists. The `/api/*` behavior uses the AWS-managed
  `Managed-AllViewerExceptHostHeader` origin request policy (forwards query
  strings/headers/bodies the BFF's endpoints actually read — e.g.
  `GET /api/gallery?series=...` — but lets CloudFront overwrite the `Host`
  header with the API Gateway origin's own domain rather than forwarding the
  viewer's CloudFront `Host`, which is the AWS-documented reason that policy
  variant exists specifically for CloudFront-in-front-of-API-Gateway/ALB
  setups) and `Managed-CachingDisabled` (mutable API responses must never be
  cached). One consequence of the same-origin design: `modules/bff-lambda`
  deliberately does **not** set `Cors:ClientOrigin` to the CloudFront
  domain — doing so would create a real dependency cycle (the Lambda's own
  bucket/NLB inputs are upstream of the CloudFront distribution that would
  supply that value), and it's functionally unnecessary anyway since
  same-origin requests never trigger a CORS check regardless of what
  `Program.cs`'s CORS policy allows.
- **Task 9.3 — SPA client-side routing: a CloudFront Function, not a
  403/404 → `index.html` error-response mapping.** The commonly suggested
  fix for Blazor WASM/SPA routing on CloudFront+S3 is a distribution-level
  `custom_error_response` mapping 403/404 to `/index.html` with response
  code 200. Considered and rejected here: `custom_error_response` applies to
  the *whole distribution*, not one cache behavior, so it would also
  intercept the BFF behavior's genuine 403/404 responses (e.g.
  `CardCatalogService.DeletePhotoAsync`'s `NotFound`, or
  `GetCollectionCardDetailsAsync` returning `Results.NotFound()`) and
  silently rewrite real API error JSON into the HTML shell instead. Instead,
  `modules/static-site` attaches a CloudFront Function (`cloudfront-js-2.0`,
  viewer-request, sub-millisecond, effectively free — no Lambda@Edge cold
  starts or cost) to *only* the default (static-site) cache behavior: if a
  request's last path segment has no `.` (doesn't look like a static file),
  it's rewritten to `/index.html` before ever reaching S3. Because CloudFront
  picks a cache behavior by path pattern before running that behavior's
  function, `/api/*` requests never execute this function at all — the
  BFF's real responses, including its real 404s, are untouched. A request
  for a genuinely missing static asset (has a dot, e.g. a stale hashed
  filename after a redeploy) still gets a plain 403/404, which is correct.
- **Task 9.4 — cold start / provisioned concurrency: not measured, and not
  enabled by default.** design.md's Risks section explicitly calls for
  *validating* real cold-start behavior before deciding whether provisioned
  concurrency is needed, and its own default assumption is that it won't be
  — this environment has no AWS credentials or live deployment to measure
  against, so that validation genuinely cannot happen from here (task 9.4 is
  the one part of task 9 left undone for that reason, not an oversight).
  `modules/bff-lambda` exposes `provisioned_concurrency` (root variable
  `bff_lambda_provisioned_concurrency`), defaulted to `0` — no
  `aws_lambda_provisioned_concurrency_config` resource is created at all
  when it's `0`. **Recommendation**: deploy with this at `0` first, measure
  real p50/p99 cold-start latency against actual traffic (VPC-attached
  Lambda cold starts have improved substantially since Hyperplane ENIs, but
  "current AWS numbers" per design.md's own open question should be checked
  against real measurements, not assumed), and only raise this dial if a
  measured number proves unacceptable for a personal-scale app with
  inherently bursty, low-frequency traffic — where provisioned concurrency's
  ongoing cost is a worse trade-off than an occasional slow first request.

## What's not here yet

Tasks 1.1–2.3 (Terraform foundations + storage), task 5 (CatalogService/
PictureService on Fargate), and task 9.1–9.3 (BFF Lambda + API Gateway, WASM
client S3 + CloudFront, single-origin routing) are done. Still outstanding:

- **Task 3**: PictureService's actual code migration to S3 + DynamoDB
  (application code, not infrastructure — this Terraform config's
  `Storage__PhotosBucketName`/`Storage__SidecarTableName` env vars and the
  task 5 IAM grants assume that code exists and behaves as read today).
- **Task 4**: the one-time `cardFotos/` → S3/DynamoDB migration script.
- **Task 5.5**: actually running `terraform apply` and verifying
  CatalogService/PictureService reach each other over Service Connect in a
  real AWS account — not done from this environment (no AWS credentials,
  Terraform binary, or AWS CLI available here). Everything in task 5 is
  written but unverified against real AWS; review carefully before the
  first real apply, especially the ECS Service Connect and internal NLB
  wiring in `modules/fargate-service` and `modules/internal-lb`.
- **Task 6–8**: `NinjagoScanner.Web.Client` and `NinjagoScanner.Web.Bff`
  already exist as C# projects with real implementations (Program.cs,
  `BffConfig.cs`, `S3UploadUrlIssuer`, the WASM client's `Program.cs` +
  `wwwroot/appsettings.json`, etc. — this Terraform config's task 9 work
  reads and depends on exactly these files) — but this `infra/` work didn't
  audit tasks 6–8's subtasks for completeness (page-by-page rewiring, the
  polling-vs-push audit, ported tests) and makes no claim about them; that's
  for whoever owns tasks 6–8 to confirm.
- **Task 9.4**: measuring actual cold-start latency for the deployed BFF
  Lambda — blocked on a real deployment (no AWS credentials here). See the
  "Task 9.4 — cold start / provisioned concurrency" design decision above
  for the recommendation left in its place (`provisioned_concurrency`
  defaulted to `0`, deploy-then-measure-then-decide).
- **Task 10**: the actual GitHub Actions workflow YAML files (build/test
  gate, four path-filtered deploy workflows, Terraform plan/apply). The
  `deploy` role's IAM policy already includes the ECR push / `ecs
  update-service` permissions (task 5) and the Lambda `update-function-code`
  / S3 sync / CloudFront invalidation permissions (task 9) those workflows
  will need (see `iam-policies.tf`'s task 5 and task 9 statement blocks) —
  only the `.github/workflows/*.yml` files themselves don't exist yet.
- **Task 11**: end-to-end verification and DNS cutover. `modules/static-site`
  exposes `domain_aliases`/`acm_certificate_arn` (root variables
  `web_client_domain_aliases`/`web_client_acm_certificate_arn`) for this —
  both empty/unset today, so the app is reachable at its `*.cloudfront.net`
  domain until task 11.4 supplies a real domain + an ACM certificate (which
  must be in `us-east-1`, a CloudFront-specific requirement regardless of
  this stack's `eu-central-1` region — see that module's `acm_certificate_arn`
  variable). Also worth tightening at that point (not required for the app
  to function, but currently deferred as a manual step — see
  `photo_bucket_cors_origins`' variable description): once the real
  CloudFront domain is known, set `photo_bucket_cors_origins` to it and
  re-apply, narrowing the photo bucket's CORS policy from `["*"]`.
