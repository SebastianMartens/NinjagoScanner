## Why

The `cloud-hosting-migration` change was fully `terraform apply`'d against the real AWS account — including the VPC/NAT Gateway, the internal NLB, both CatalogService/PictureService Fargate services, the Web BFF Lambda, and the WASM client's CloudFront distribution — none of which ever carried real traffic (task 5.5/9.4/11 verification never happened). Compute is moving to Fly.io in a follow-up change, which makes all of that AWS compute infrastructure dead weight that's actively billing (NAT Gateway and the NLB especially). There is no live customer traffic today, so this is the cheapest possible moment to remove it: no cutover risk, only an accepted downtime window until the Fly.io change lands.

## What Changes

- **BREAKING**: The app becomes fully unreachable (no compute anywhere) once this change is applied, until the follow-up Fly.io change stands up replacement compute. Accepted — there is no live traffic today.
- Destroy the real AWS resources for: `networking` (VPC, subnets, IGW, NAT Gateway(s), S3/DynamoDB gateway endpoints), `ecs_cluster` (+ Service Connect namespace), both `fargate_service` instances (CatalogService, PictureService), `internal_lb`, `bff_lambda` (+ API Gateway), `web_client`/`static-site` (+ CloudFront distribution), both `ecr_repo` instances (CatalogService, PictureService), and `secrets` (the Gemini API key Secrets Manager entry).
- Remove the corresponding Terraform modules and their wiring from `infra/environments/prod/main.tf`, and delete the now-unused module directories under `infra/modules/`.
- Narrow `github-oidc`'s attached IAM policies (`infra/environments/prod/iam-policies.tf`) down to only what the kept storage stack (`photo_storage`, `sidecar_table`, state-backend) needs — the ECS/Fargate/Lambda/CloudFront/ECR permission statements are removed, not just the resources they pointed at.
- Remove `infra/environments/prod/ecs-task-policies.tf` (PictureService's ECS task-role policy) — irrelevant once PictureService no longer runs on ECS.
- Delete the AWS-compute-targeting GitHub Actions workflows: `deploy-catalog-service.yml`, `deploy-picture-service.yml`, `_deploy-ecs-service.yml`, `deploy-web-bff.yml`, `deploy-web-client.yml`. `terraform.yml` and `ci.yml` are kept, updated to reflect the smaller stack.
- Update `infra/README.md` to describe the post-teardown stack (storage-only) instead of the full compute architecture it documents today.
- **Kept, unchanged**: the S3 photo bucket (`photo_storage`), the DynamoDB sidecar table (`sidecar_table`), the Terraform state backend (`bootstrap`), and all data in them.

## Capabilities

This is an infrastructure-only change (Terraform, CI workflow files, infra docs) — it does not change any service's observable behavior, API surface, or spec-level requirements. No application code is touched. Per the proposal guidance, this change sets `skip_specs: true` rather than declaring a capability delta.

## Impact

- **`infra/environments/prod/main.tf`**: seven module blocks removed (`networking`, `ecs_cluster`, `catalog_service`, `picture_service`, `internal_lb`, `bff_lambda`, `web_client`, `ecr_catalog_service`, `ecr_picture_service`, `secrets` — `github_oidc`, `photo_storage`, `sidecar_table` kept and rewired).
- **`infra/environments/prod/iam-policies.tf`**: trimmed to storage-only permission statements.
- **`infra/environments/prod/ecs-task-policies.tf`**: deleted.
- **`infra/modules/{networking,ecs-cluster,fargate-service,internal-lb,bff-lambda,static-site,ecr-repo,secrets}/`**: deleted (no longer referenced by any environment).
- **`.github/workflows/`**: 5 files deleted, `terraform.yml` updated.
- **`infra/README.md`**: updated to describe the storage-only stack.
- **Real AWS account**: the resources listed under "What Changes" are destroyed for real via `terraform apply` — this is the one step in this change that touches live infrastructure and should be reviewed via `terraform plan` output before applying, even though downtime is accepted.
- **No impact** to `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`, `NinjagoScanner.Web.Bff`, `NinjagoScanner.Web.Client`, or any `openspec/specs/*` capability — application code and specs are unchanged.
