# Infrastructure (Terraform)

AWS infrastructure for NinjagoScanner. As of the `aws-compute-teardown`
OpenSpec change (see `openspec/changes/aws-compute-teardown/{proposal,design,tasks}.md`),
this stack is storage-only: the Terraform state backend, the S3 photo
bucket, the DynamoDB sidecar table, and the GitHub Actions OIDC roles that
manage them. It previously also ran the app's compute (VPC, ECS Fargate,
an internal NLB, a Web BFF Lambda, and a CloudFront-fronted WASM client —
the `cloud-hosting-migration` change) but that was torn down: it never
carried real production traffic, and compute is moving to Fly.io in a
follow-up change instead. See `aws-compute-teardown`'s proposal for the
full "why."

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
                                IAM roles.
    photo-storage/               S3 bucket for card photos: versioning,
                                  CORS (for direct browser upload), lifecycle
                                  rules.
    sidecar-table/                 DynamoDB table for sidecar records + GSIs.
```

`environments/prod/main.tf` composes `photo-storage`, `sidecar-table`, and
`github-oidc` into one deployable stack. `environments/prod/iam-policies.tf`
builds the IAM policy documents attached to the two GitHub Actions roles,
since only the root module knows every resource ARN those policies need to
reference.

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

4. From here on, GitHub Actions workflows (`.github/workflows/terraform.yml`)
   can run `terraform plan`/`apply` against this same config using the
   `deploy` role via OIDC — no AWS keys stored in GitHub.

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
    `main`; write permissions. Used for `terraform apply`.
  A pull request — including from a fork — can therefore never assume a
  role capable of changing real infrastructure.
- **IAM policy scope**: both roles' policies cover exactly the resources
  this Terraform config manages today (the S3 photo bucket, the DynamoDB
  sidecar table, the OIDC provider/roles themselves) plus read/write access
  to the Terraform state backend — see the comment block at the top of
  `environments/prod/iam-policies.tf`.
- **DynamoDB sidecar table**: `PAY_PER_REQUEST` billing (no capacity
  planning), point-in-time recovery on, deletion protection on by default.
  Partition key `PhotoId` (a generated ID, not the original filename — see
  design.md's "Photo identity" decision in `cloud-hosting-migration`), with
  three GSIs matching the app's three real query patterns today: by review
  status, by analysis status, and by series+card-number (how "Owned Copies"
  is computed — see `openspec/GLOSSARY.md`). See the comment block at the
  top of `modules/sidecar-table/main.tf` for the full reasoning, including a
  naming note: the table's `SeriesName` attribute corresponds to what
  PictureService's current C# code calls `SetName`.
- **S3 photo bucket**: versioning + SSE-S3 encryption + all-public-access
  blocked, with a CORS rule (currently `allowed_origins = ["*"]`) so the
  browser can `PUT` directly to S3 using a presigned URL. Tighten this to
  the app's actual origin(s) once compute exists again — see
  `photo_bucket_cors_origins`'s variable description. Lifecycle rules abort
  stale multipart uploads after 7 days and expire noncurrent object versions
  after 30 days — bounding storage growth from the versioning safety net
  without touching the current (actively viewed) version of any photo.
- **Self-managing IAM role.** The `deploy` role's policy grants it rights to
  manage its own role/policy and the OIDC provider (scoped tightly to those
  exact resource names — see `iam-policies.tf`'s `SelfManagedIamRoles` /
  `SelfManagedOidcProvider` statements — never `iam:*`). This lets future
  Terraform changes to the CI role itself go through the same
  plan-on-PR/apply-on-main pipeline as everything else, at the cost of the
  bootstrapping wrinkle described above (first apply must be run by a
  human).

## What's not here yet

AWS compute is **deliberately** not part of this stack, and won't be added
back here — `aws-compute-teardown` removed the VPC/ECS Fargate/internal
NLB/BFF Lambda/CloudFront setup that `cloud-hosting-migration` had stood up,
since it never carried real traffic and was actively billing for no
benefit. Compute is moving to Fly.io instead; see
`openspec/changes/fly-hosting-migration` (or its successor) for that work
and whatever IaC approach it settles on — this `infra/` directory is
expected to stay storage-only (S3 + DynamoDB + the Terraform/CI plumbing
around them) going forward.
