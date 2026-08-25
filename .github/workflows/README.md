# GitHub Actions workflows

Two workflows, plus infra-as-code CI:

| File | Trigger |
|---|---|
| `ci.yml` | PR + push to `main`: `dotnet build` + `dotnet test` on the whole solution |
| `terraform.yml` | PR (plan) / push to `main` (apply), paths `infra/**` |

`ci.yml` needs no AWS access. `terraform.yml` authenticates via GitHub OIDC (no long-lived AWS
keys — see `infra/modules/github-oidc`), using one of two IAM roles depending on the job:

- **plan** role (`AWS_PLAN_ROLE_ARN` secret): read-only, used by the PR-triggered plan job.
  Assumable from pull requests, including forks.
- **deploy** role (`AWS_DEPLOY_ROLE_ARN` secret): write access, used by the apply job on pushes
  to `main`. Assumable **only** from pushes to `main` — a PR can never assume it.

As of the `aws-compute-teardown` change, `infra/` only manages storage (S3 photo bucket, DynamoDB
sidecar table) and the Terraform state backend/OIDC roles — there is no AWS compute stack and no
per-service deploy workflow. Compute is not part of this repo's AWS footprint; see
`openspec/changes/fly-hosting-migration` (or its successor) for wherever CI/CD for actual service
deploys ends up living.

## One-time setup, once `infra/` has been applied at least once by hand

None of this exists until someone runs the manual first `terraform apply` described in
`infra/README.md`'s "Bootstrapping" section (this workflow deliberately can't do that itself —
see that section for why). After that apply, set these as **repository variables**
(Settings → Secrets and variables → Actions → Variables) from the corresponding
`terraform output` in `infra/environments/prod`:

| Variable | From `terraform output` |
|---|---|
| `AWS_REGION` | (the region you deployed to, e.g. `eu-central-1`) |
| `TF_STATE_BUCKET_NAME` / `TF_STATE_LOCK_TABLE_NAME` | from `infra/bootstrap`'s outputs |
| `TF_STATE_BUCKET_ARN` / `TF_STATE_LOCK_TABLE_ARN` | from `infra/bootstrap`'s outputs |

And these as **repository secrets**:

| Secret | From `terraform output` |
|---|---|
| `AWS_PLAN_ROLE_ARN` | `github_actions_plan_role_arn` |
| `AWS_DEPLOY_ROLE_ARN` | `github_actions_deploy_role_arn` |

`terraform.yml`'s apply job also targets a GitHub Environment named `production` — create one
(Settings → Environments) if you want required reviewers/wait timers on infra applies; it works
with no extra configuration otherwise.

Until these variables/secrets exist, only `ci.yml` will run successfully — `terraform.yml` will
fail fast on a missing role ARN, which is expected before the infra has been bootstrapped.
