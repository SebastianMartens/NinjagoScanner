## 1. Reshape Terraform to the desired end-state

- [x] 1.1 In `infra/environments/prod/main.tf`, remove the `networking`, `ecs_cluster`, `catalog_service`, `picture_service`, `internal_lb`, `bff_lambda`, `web_client`, `ecr_catalog_service`, `ecr_picture_service`, and `secrets` module blocks (and the `locals` block used only by them: `catalog_service_name`, `picture_service_name`, `service_container_port`, `service_health_check_port`, `catalog_service_listener_port`, `picture_service_listener_port`). Keep `photo_storage`, `sidecar_table`, `github_oidc` wired as-is.
- [x] 1.2 Delete `infra/environments/prod/ecs-task-policies.tf`.
- [x] 1.3 In `infra/environments/prod/iam-policies.tf`, remove the ECS/Fargate/Lambda/CloudFront/ECR statements from `manage_resources_fargate`, `manage_resources_web_hosting`, and `manage_resources_fargate_platform` (delete these `aws_iam_policy_document` data sources entirely if nothing storage-related remains in them); keep `deploy_core`'s state-backend and self-managed-IAM-role/OIDC-provider statements, and keep storage-specific statements (S3 photo bucket, DynamoDB sidecar table) wherever they currently live.
- [x] 1.4 Update `module.github_oidc`'s `deploy_policy_jsons` list in `main.tf` to reference only the policy documents that still exist after 1.3.
- [x] 1.5 Remove any now-unused variables from `infra/environments/prod/variables.tf`/`outputs.tf`/`terraform.tfvars`/`terraform.tfvars.example` that only served the removed modules (e.g. `catalog_service_image_tag`, `picture_service_image_tag`, `catalog_service_cpu`/`memory`/`desired_count`, `picture_service_cpu`/`memory`/`desired_count`, `web_client_cloudfront_price_class`, `web_client_domain_aliases`, `web_client_acm_certificate_arn`, `bff_lambda_*`, `vpc_cidr`, `az_count`, `single_nat_gateway`, `photo_bucket_cors_origins` only if it was compute-facing — verify against what `photo_storage` still needs).
- [x] 1.6 Run `terraform fmt` and `terraform validate` in `infra/environments/prod` to confirm the edited config is syntactically and internally consistent before planning against real state.

## 2. Review and apply against real AWS

- [x] 2.1 Run `terraform plan` in `infra/environments/prod` and read the full output.
- [x] 2.2 Confirm the plan shows destroy-only changes for exactly: VPC/subnets/IGW/NAT Gateway(s)/gateway endpoints, the ECS cluster + Service Connect namespace, both Fargate services + task/execution IAM roles, the internal NLB + target groups + security groups, the Lambda function + its IAM role + API Gateway, the CloudFront distribution + OAC + the WASM static-site S3 bucket, both ECR repositories, and the Secrets Manager secret — and zero changes to any `photo_storage`/`sidecar_table`/`bootstrap`-owned resource.
- [x] 2.3 If the plan shows anything unexpected (drift, a resource not accounted for in 2.2), stop and investigate before applying.
- [x] 2.4 Run `terraform apply` and confirm it completes successfully.
- [x] 2.5 Spot-check the AWS console (or `aws` CLI) for the account to confirm the VPC, ECS cluster, Lambda function, and CloudFront distribution are gone, and that the S3 photo bucket and DynamoDB sidecar table are still present with their data intact.

## 3. Remove dead Terraform module code

- [x] 3.1 Delete `infra/modules/networking/`, `infra/modules/ecs-cluster/`, `infra/modules/fargate-service/`, `infra/modules/internal-lb/`, `infra/modules/bff-lambda/`, `infra/modules/static-site/`, `infra/modules/ecr-repo/`, `infra/modules/secrets/`.
- [x] 3.2 Confirm `infra/modules/state-backend/`, `infra/modules/photo-storage/`, `infra/modules/sidecar-table/`, `infra/modules/github-oidc/`, and `infra/bootstrap/` are untouched.

## 4. Retire AWS-compute CI/CD

- [x] 4.1 Delete `.github/workflows/deploy-catalog-service.yml`, `.github/workflows/deploy-picture-service.yml`, `.github/workflows/_deploy-ecs-service.yml`, `.github/workflows/deploy-web-bff.yml`, `.github/workflows/deploy-web-client.yml`.
- [x] 4.2 Review `.github/workflows/terraform.yml` and `.github/workflows/ci.yml` for any references to the removed workflows, variables, or resources (e.g. path filters, job dependencies) and update them to match the storage-only stack.

## 5. Update documentation

- [x] 5.1 Rewrite `infra/README.md`'s "Layout" section to list only the kept modules (`state-backend`, `photo-storage`, `sidecar-table`, `github-oidc`) and remove all "Design decisions worth knowing about" bullets that only apply to removed resources (VPC/NAT, ECS/Fargate, internal NLB, BFF Lambda, static-site/CloudFront).
- [x] 5.2 Update `infra/README.md`'s "What's not here yet" section to reflect that AWS compute is deliberately not part of this stack going forward (Fly.io follow-up change owns compute).
