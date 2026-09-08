# Infrastructure (Terraform)

AWS infrastructure for NinjagoScanner. As of the `aws-compute-teardown`
OpenSpec change (see `openspec/changes/aws-compute-teardown/{proposal,design,tasks}.md`),
this stack is storage-only: the Terraform state backend, the S3 photo
bucket, the DynamoDB sidecar table, the IAM user PictureService uses to
reach them, and the GitHub Actions OIDC roles that manage all of it. It
previously also ran the app's compute (VPC, ECS Fargate, an internal NLB, a
Web BFF Lambda, and a CloudFront-fronted WASM client — the
`cloud-hosting-migration` change) but that was torn down: it never carried
real production traffic. Compute now runs on Fly.io instead (the
`fly-hosting-migration` change) — see "What's not here yet" below for how
that's configured, since it isn't Terraform-managed.

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
    iam-user/                        IAM user + scoped policy PictureService
                                       authenticates to AWS with on Fly.io.
```

`environments/prod/main.tf` composes `photo-storage`, `sidecar-table`,
`iam-user`, and `github-oidc` into one deployable stack. `environments/prod/iam-policies.tf`
builds the IAM policy documents attached to the two GitHub Actions roles,
since only the root module knows every resource ARN those policies need to
reference.

Every resource is tagged `Project = "ninjago-scanner"`, `ManagedBy =
"terraform"`, `Environment = "prod"` (see `local.common_tags` in
`environments/prod/main.tf`, applied via the AWS provider's `default_tags`).

## State & locking

Terraform state lives in S3 (bucket created by `bootstrap/`), locked via
Terraform's native S3 lockfile mechanism (`use_lockfile = true` in
`backend.hcl` — see `backend.hcl.example` and `bootstrap`'s
`state_backend_config` output) so two `terraform apply` runs (e.g. a human
and a CI job) can't race each other. `environments/prod` never manages the
bucket it stores its own state in — that would be circular — it only
consumes it as a backend.

`modules/state-backend` also provisions a DynamoDB lock table
(`lock_table_name`/`lock_table_arn`), a holdover from the legacy
DynamoDB-digest locking scheme. It's no longer used for locking — every
`backend.hcl` must use `use_lockfile`, not `dynamodb_table` — but is kept
around (and the CI IAM roles still hold scoped `dynamodb:*Item` permissions
on it) rather than torn down. **Never mix the two mechanisms across
`backend.hcl` files for the same state key**: a `dynamodb_table` backend
writes/checks an MD5 digest of the state on every read, but a
`use_lockfile` backend never updates that digest, so an apply from a
`use_lockfile` backend followed by a read from a `dynamodb_table` backend
fails with a checksum mismatch (`state data in S3 does not have the
expected content`) until the stale digest is manually corrected in
DynamoDB.

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
- **PictureService's own IAM user (`modules/iam-user`)**: a static IAM user
  with an inline policy scoped to exactly the S3/DynamoDB actions
  PictureService's code calls — `s3:GetObject`/`PutObject`/`DeleteObject` on
  the photo bucket's `photos/*` prefix, `s3:ListBucket` on the bucket
  (restricted to that prefix via a condition), and
  `dynamodb:GetItem`/`PutItem`/`DeleteItem`/`Scan` on the sidecar table. Fly
  Machines have no AWS-native way to assume an IAM role the way an ECS task
  did, so this is a static access key/secret instead of auto-rotated STS
  credentials — a real security posture downgrade, accepted as a pragmatic
  trade-off for a personal-scale app (see
  `openspec/changes/fly-hosting-migration/design.md` Decision 4). No other
  service gets an IAM user; PictureService remains the only service that
  ever holds AWS credentials.
- **Self-managing IAM role.** The `deploy` role's policy grants it rights to
  manage its own role/policy and the OIDC provider (scoped tightly to those
  exact resource names — see `iam-policies.tf`'s `SelfManagedIamRoles` /
  `SelfManagedOidcProvider` statements — never `iam:*`). This lets future
  Terraform changes to the CI role itself go through the same
  plan-on-PR/apply-on-main pipeline as everything else, at the cost of the
  bootstrapping wrinkle described above (first apply must be run by a
  human).

## Setting PictureService's AWS credentials as a Fly secret

`modules/iam-user`'s access key is the one piece of this stack's output
that has to leave Terraform and land in Fly rather than in another AWS
resource. After `terraform apply`:

```powershell
cd infra/environments/prod
terraform output picture_service_access_key_id
terraform output -raw picture_service_secret_access_key
flyctl secrets set --config ../../../NinjagoScanner.PictureService/fly.toml `
  AWS_ACCESS_KEY_ID=<value from the first output> `
  AWS_SECRET_ACCESS_KEY=<value from the second output>
```

Also set PictureService's Gemini credentials and the bucket/table names the
same way (`Gemini__ApiKey`, `Gemini__Model`, `Storage__PhotosBucketName` —
from `terraform output photo_bucket_name` —, `Storage__SidecarTableName` —
from `terraform output collection_sidecar_table_name`, **not**
`sidecar_table_name` — see the next section), matching how the Gemini key is
already handled: never committed, set once as a secret on the running app.

## Two sidecar tables (add-collection-data-isolation migration)

`modules/sidecar-table` (output `sidecar_table_name`) and
`modules/collection-sidecar-table` (output `collection_sidecar_table_name`)
are two separate DynamoDB tables, not a versioned pair — see the comment
block at the top of `modules/collection-sidecar-table/main.tf` for why a
second table exists instead of editing the first one's key schema in place.
`collection_sidecar_table_name` is PictureService's live table going
forward (point `Storage__SidecarTableName` at it); `sidecar_table_name`
is kept only so the one-time `NinjagoScanner.CollectionAssignmentMigration`
tool can read the old data (`--old-table`) and for eventual manual
decommissioning once that migration is verified in production. Don't
point a running PictureService at `sidecar_table_name` after cutover.

## AWS compute (what's not here) vs. Fly.io compute (what's not Terraform)

AWS compute is **deliberately** not part of this stack, and won't be added
back here — `aws-compute-teardown` removed the VPC/ECS Fargate/internal
NLB/BFF Lambda/CloudFront setup that `cloud-hosting-migration` had stood up,
since it never carried real traffic and was actively billing for no
benefit. This `infra/` directory stays storage-only (S3 + DynamoDB + the
IAM user above + the Terraform/CI plumbing around them) going forward.

Compute now runs on Fly.io instead (`fly-hosting-migration`), but Fly
resources are **not** managed by this Terraform config — `fly.toml` per
project (`NinjagoScanner.Web/fly.toml`, `NinjagoScanner.CatalogService/fly.toml`,
`NinjagoScanner.PictureService/fly.toml`) plus `flyctl` is Fly's own
first-party config-as-code, and is what this repo uses instead (see
`openspec/changes/fly-hosting-migration/design.md` Decision 5 for why the
unofficial Terraform Fly provider was rejected). See each project's
`fly.toml` for its own configuration detail rather than looking for it
here.
