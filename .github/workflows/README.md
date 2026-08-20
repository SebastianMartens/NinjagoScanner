# GitHub Actions workflows

Implements task 10 of `openspec/changes/cloud-hosting-migration`. Six workflows:

| File | Task | Trigger |
|---|---|---|
| `ci.yml` | 10.1 | PR + push to `main`: `dotnet build` + `dotnet test` on the whole solution |
| `deploy-catalog-service.yml` | 10.2 | Push to `main`, paths `NinjagoScanner.CatalogService/**` |
| `deploy-picture-service.yml` | 10.3 | Push to `main`, paths `NinjagoScanner.PictureService/**` |
| `deploy-web-bff.yml` | 10.4 | Push to `main`, paths `NinjagoScanner.Web.Bff/**`, `NinjagoScanner.Web.Shared/**` |
| `deploy-web-client.yml` | 10.5 | Push to `main`, paths `NinjagoScanner.Web.Client/**`, `NinjagoScanner.Web.Shared/**` |
| `terraform.yml` | 10.6 | PR (plan) / push to `main` (apply), paths `infra/**` |
| `_deploy-ecs-service.yml` | — | Reusable workflow shared by the two Fargate deploys, not triggered directly |

`ci.yml` needs no AWS access. Every other workflow authenticates via GitHub OIDC (no long-lived
AWS keys — see `infra/modules/github-oidc`), using one of two IAM roles depending on what it does:

- **plan** role (`AWS_PLAN_ROLE_ARN` secret): read-only, used by `terraform.yml`'s PR-triggered
  plan job. Assumable from pull requests, including forks.
- **deploy** role (`AWS_DEPLOY_ROLE_ARN` secret): write access, used by every `main`-triggered
  job (all five deploy workflows + `terraform.yml`'s apply job). Assumable **only** from pushes
  to `main` — a PR can never assume it.

## One-time setup, once `infra/` has been applied at least once by hand

None of this exists until someone runs the manual first `terraform apply` described in
`infra/README.md`'s "Bootstrapping" section (these workflows deliberately can't do that
themselves — see that section for why). After that apply, set these as **repository variables**
(Settings → Secrets and variables → Actions → Variables) from the corresponding
`terraform output` in `infra/environments/prod`:

| Variable | From `terraform output` |
|---|---|
| `AWS_REGION` | (the region you deployed to, e.g. `eu-central-1`) |
| `TF_STATE_BUCKET_NAME` / `TF_STATE_LOCK_TABLE_NAME` | from `infra/bootstrap`'s outputs |
| `TF_STATE_BUCKET_ARN` / `TF_STATE_LOCK_TABLE_ARN` | from `infra/bootstrap`'s outputs |
| `ECR_CATALOG_SERVICE_REPOSITORY_URL` | `ecr_catalog_service_repository_url` |
| `ECR_PICTURE_SERVICE_REPOSITORY_URL` | `ecr_picture_service_repository_url` |
| `ECS_CLUSTER_NAME` | `ecs_cluster_name` |
| `LAMBDA_BFF_FUNCTION_NAME` | task 9's Lambda module output |
| `WASM_CLIENT_BUCKET_NAME` | task 9's static-site module output |
| `CLOUDFRONT_DISTRIBUTION_ID` | task 9's static-site module output |

And these as **repository secrets**:

| Secret | From `terraform output` |
|---|---|
| `AWS_PLAN_ROLE_ARN` | `github_actions_plan_role_arn` |
| `AWS_DEPLOY_ROLE_ARN` | `github_actions_deploy_role_arn` |

`terraform.yml`'s apply job also targets a GitHub Environment named `production` — create one
(Settings → Environments) if you want required reviewers/wait timers on infra applies; it works
with no extra configuration otherwise.

Until these variables/secrets exist, only `ci.yml` will run successfully — the rest will fail
fast on a missing role ARN, which is expected before the infra has been bootstrapped.
