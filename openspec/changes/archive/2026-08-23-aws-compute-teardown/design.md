## Context

See proposal.md - Why. Relevant constraints for the approach:

- `infra/environments/prod` is **one Terraform root module** with **one state file** covering everything — storage (`photo_storage`, `sidecar_table`) and compute (`networking`, `ecs_cluster`, `fargate_service` x2, `internal_lb`, `bff_lambda`, `web_client`/`static-site`, `ecr_repo` x2, `secrets`) live in the same `terraform apply`. There is no separate state per subsystem.
- Verified from `infra/environments/prod/main.tf`: neither `photo_storage` nor `sidecar_table` takes any input from `networking` (no VPC ID, no subnet IDs, no security group). S3 and DynamoDB are regional services, not VPC-attached — removing the VPC module cannot orphan them.
- `github_oidc`'s IAM policies are assembled in `infra/environments/prod/iam-policies.tf` from four separate `aws_iam_policy_document` data sources (`plan`, `deploy_core`, `manage_resources_fargate`, `manage_resources_web_hosting`, `manage_resources_fargate_platform`) merged into the `deploy` role. This module is not being removed — its policy documents need trimming, not its existence.

## Goals / Non-Goals

**Goals:**
- Destroy every real AWS resource this stack created for compute (VPC/NAT/NLB/ECS/Fargate/Lambda/CloudFront/ECR/Secrets Manager), verified via `terraform plan` before `terraform apply`.
- Leave the storage stack (S3 photo bucket, DynamoDB sidecar table, Terraform state backend) and its data completely untouched.
- Leave the repo in a state where `infra/environments/prod` only describes what's actually kept — no orphaned module references, no stale IAM permissions for resources that no longer exist.

**Non-Goals:**
- Standing up Fly.io compute — that's the follow-up change (`fly-hosting-migration` or similar), not this one.
- Changing any application code, gRPC contract, or spec-level behavior of CatalogService/PictureService/Web.
- Deciding Fly.io's own IaC approach (Terraform provider vs. `fly.toml`) — out of scope here, will be decided in the follow-up change.

## Decisions

**1. Edit Terraform code to the desired end-state first, then `plan`/`apply` once — not manual `-target` destroys.**
Removing the seven compute-related module blocks from `main.tf` (and their supporting `iam-policies.tf`/`ecs-task-policies.tf` references) makes the *code* describe the desired end-state directly. A single `terraform plan` against existing state then shows exactly the resources that will be destroyed to reconcile reality with that code — reviewable as one diff — rather than a human picking `-target` resource addresses by hand and risking missing one (e.g. forgetting the S3/DynamoDB gateway endpoints that live inside the `networking` module). This is the standard, safer Terraform pattern for "delete this subsystem" and matches how the module was additively built up in the first place.

**2. Keep `github-oidc` and `terraform.yml`, trim the IAM policy scope rather than removing CI automation.**
Alternative considered: drop GitHub OIDC + `terraform.yml` entirely now that the stack is "just" two storage resources, and apply changes to `photo_storage`/`sidecar_table` by hand going forward (matches the project's existing personal-scale, cost-conscious posture elsewhere — e.g. single NAT Gateway). Rejected: the OIDC provider/roles already exist and cost nothing to keep; removing them is strictly more work than trimming their attached policy statements, and losing plan-on-PR review for future storage changes (e.g. altering the sidecar table's GSIs) is a real capability loss for a two-resource stack that's still worth reviewing before applying. `deploy_core` (state-backend read/write, self-managed IAM role/OIDC provider statements) and the storage-specific statements inside `manage_resources_*` are kept; `manage_resources_fargate`, `manage_resources_web_hosting`, and `manage_resources_fargate_platform`'s ECS/Lambda/CloudFront/ECR statements are removed as those documents become empty or are deleted outright.

**3. Delete unused module directories, don't just stop referencing them.**
`infra/modules/{networking,ecs-cluster,fargate-service,internal-lb,bff-lambda,static-site,ecr-repo,secrets}/` are deleted outright rather than left in the tree unreferenced. Nothing else in this repo's Terraform will ever instantiate them again post-teardown (Fly.io's follow-up change uses different tooling entirely — see Non-Goals), so keeping dead module code around only invites confusion about whether it's still load-bearing.

**4. `terraform destroy`-equivalent via `plan`+`apply`, not the `terraform destroy` command.**
Because storage modules stay in the same root module/state, running `terraform destroy` (which tears down everything in scope) is the wrong tool — it would also destroy `photo_storage`/`sidecar_table`. The correct operation is editing the code to the desired state and running `terraform apply` (which computes an add/change/destroy plan per-resource) — `terraform destroy` is never invoked in this change's task list.

## Risks / Trade-offs

- **[Risk] A `terraform plan` run against real state might surface unexpected drift** (e.g. a resource manually modified outside Terraform since the 2026-08-20 apply) that changes what gets destroyed or reveals a resource this design doesn't account for. → **Mitigation**: the task list requires reading the full `terraform plan` output and confirming it matches the expected resource list (see proposal.md's "What Changes") before running `apply` — not applying blind.
- **[Risk] Destroying the VPC/NAT Gateway/NLB is irreversible in the sense that recreating them later means new IDs, new DNS names, new IAM ARNs** — anything hardcoded elsewhere (there shouldn't be any, since Terraform manages all of it, but worth stating). → **Mitigation**: none needed beyond normal Terraform review; nothing outside this Terraform config is expected to reference these resources by ID.
- **[Trade-off] Downtime for the whole app** from the moment this change is applied until the Fly.io follow-up change lands. → **Mitigation**: none — explicitly accepted in proposal.md given no live traffic today.

## Migration Plan

1. Edit `infra/environments/prod/main.tf`, `iam-policies.tf`, delete `ecs-task-policies.tf` to reach the desired end-state code (Decision 1).
2. `terraform plan` — read the full output, confirm it matches the expected destroy list (Risk 1's mitigation) and shows zero changes to `photo_storage`/`sidecar_table`/`bootstrap` resources.
3. `terraform apply` — the one live-infrastructure-touching step in this change.
4. Delete the unused `infra/modules/*` directories (Decision 3) and the five AWS-compute GitHub Actions workflow files (proposal.md's "What Changes").
5. Update `infra/README.md` to describe the resulting storage-only stack.

No rollback beyond standard Terraform (re-adding the module blocks and re-applying, which recreates the resources from scratch — not a true rollback, since e.g. a new NAT Gateway gets a new IP). Acceptable given Non-Goals: this change is deliberately one-way, with Fly.io as the forward path, not a revert path.
