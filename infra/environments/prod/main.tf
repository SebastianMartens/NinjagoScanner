locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }
}

module "networking" {
  source = "../../modules/networking"

  project_name       = var.project_name
  aws_region         = var.aws_region
  vpc_cidr           = var.vpc_cidr
  az_count           = var.az_count
  single_nat_gateway = var.single_nat_gateway
  tags               = local.common_tags
}

module "photo_storage" {
  source = "../../modules/photo-storage"

  project_name         = var.project_name
  cors_allowed_origins = var.photo_bucket_cors_origins
  tags                 = local.common_tags
}

module "sidecar_table" {
  source = "../../modules/sidecar-table"

  project_name                = var.project_name
  deletion_protection_enabled = var.sidecar_table_deletion_protection
  tags                        = local.common_tags
}

module "secrets" {
  source = "../../modules/secrets"

  project_name = var.project_name
  environment  = var.environment
  tags         = local.common_tags
}

module "github_oidc" {
  source = "../../modules/github-oidc"

  github_repo        = var.github_repo
  deploy_branch      = var.deploy_branch
  plan_role_name     = var.github_plan_role_name
  deploy_role_name   = var.github_deploy_role_name
  plan_policy_jsons = [data.aws_iam_policy_document.plan.json]
  deploy_policy_jsons = [
    data.aws_iam_policy_document.deploy_core.json,
    data.aws_iam_policy_document.manage_resources_fargate.json,
    data.aws_iam_policy_document.manage_resources_web_hosting.json,
  ]
  tags               = local.common_tags
}

# ---- CatalogService and PictureService on Fargate (task 5) ----
# Service Connect discovery names double as each service's short name
# throughout this stack (ECR repo suffix, ECS service name, IAM role names,
# log group). Keeping them as locals (not variables) — they're internal
# wiring other resources' names are derived from, not a deployment-time
# choice like image tags or sizing.
locals {
  catalog_service_name   = "catalog-service"
  picture_service_name   = "picture-service"
  service_container_port = 8080

  # The internal NLB's two listener ports (modules/internal-lb's `targets`
  # list below) — pulled into locals so the BFF Lambda module (task 9) can
  # be pointed at the same values without hardcoding them a second time.
  catalog_service_listener_port = 8080
  picture_service_listener_port = 8081
}

module "ecr_catalog_service" {
  source = "../../modules/ecr-repo"

  repository_name = "${var.project_name}-${local.catalog_service_name}"
  tags            = local.common_tags
}

module "ecr_picture_service" {
  source = "../../modules/ecr-repo"

  repository_name = "${var.project_name}-${local.picture_service_name}"
  tags            = local.common_tags
}

module "ecs_cluster" {
  source = "../../modules/ecs-cluster"

  project_name = var.project_name
  tags         = local.common_tags
}

# Task 5.4: neither service is public (see modules/internal-lb's header
# comment for the full reasoning) — this is the private, VPC-only path the
# future BFF Lambda (task 9) will use to reach both services directly.
module "internal_lb" {
  source = "../../modules/internal-lb"

  project_name       = var.project_name
  vpc_id             = module.networking.vpc_id
  private_subnet_ids = module.networking.private_subnet_ids
  ingress_cidr       = module.networking.vpc_cidr

  targets = [
    {
      name              = local.catalog_service_name
      listener_port     = local.catalog_service_listener_port
      target_port       = local.service_container_port
      health_check_path = "/"
    },
    {
      name              = local.picture_service_name
      listener_port     = local.picture_service_listener_port
      target_port       = local.service_container_port
      health_check_path = "/"
    },
  ]

  tags = local.common_tags
}

module "catalog_service" {
  source = "../../modules/fargate-service"

  project_name       = var.project_name
  service_name       = local.catalog_service_name
  cluster_id         = module.ecs_cluster.cluster_id
  vpc_id             = module.networking.vpc_id
  vpc_cidr           = module.networking.vpc_cidr
  private_subnet_ids = module.networking.private_subnet_ids

  ecr_repository_arn = module.ecr_catalog_service.repository_arn
  container_image    = "${module.ecr_catalog_service.repository_url}:${var.catalog_service_image_tag}"
  container_port     = local.service_container_port
  cpu                = var.catalog_service_cpu
  memory             = var.catalog_service_memory
  desired_count      = var.catalog_service_desired_count

  # No AWS resource access beyond default ECS/CloudWatch Logs permissions —
  # see ecs-task-policies.tf's header comment for why.
  create_task_role = false

  service_connect_namespace_arn  = module.ecs_cluster.service_connect_namespace_arn
  service_connect_discovery_name = local.catalog_service_name

  load_balancer_target_group_arn = module.internal_lb.target_group_arns[local.catalog_service_name]

  tags = local.common_tags
}

module "picture_service" {
  source = "../../modules/fargate-service"

  project_name       = var.project_name
  service_name       = local.picture_service_name
  cluster_id         = module.ecs_cluster.cluster_id
  vpc_id             = module.networking.vpc_id
  vpc_cidr           = module.networking.vpc_cidr
  private_subnet_ids = module.networking.private_subnet_ids

  ecr_repository_arn = module.ecr_picture_service.repository_arn
  container_image    = "${module.ecr_picture_service.repository_url}:${var.picture_service_image_tag}"
  container_port     = local.service_container_port
  cpu                = var.picture_service_cpu
  memory             = var.picture_service_memory
  desired_count      = var.picture_service_desired_count

  environment_variables = {
    # See ScannerConfig.ResolvePhotosBucketName/ResolveSidecarTableName —
    # PictureService throws at startup if either is unset.
    "Storage__PhotosBucketName" = module.photo_storage.bucket_name
    "Storage__SidecarTableName" = module.sidecar_table.table_name
    # Resolved by CatalogGrpcClient.cs / ScannerConfig.cs's
    # CatalogService:Address — reachable because both services are
    # Service-Connect-enabled in the same namespace (task 5.3).
    "CatalogService__Address" = "http://${local.catalog_service_name}:${local.service_container_port}"
  }

  # Gemini:ApiKey / Gemini:Model (double-underscore env var convention maps
  # to .NET config's colon-separated keys — see GeminiApiService.cs /
  # ScannerConfig.cs). Sourced from JSON keys "ApiKey"/"Model" (not
  # "Gemini:ApiKey"/"Gemini:Model") inside the one Secrets Manager secret —
  # see infra/README.md's Secrets Manager section for why the JSON keys
  # themselves must not contain colons.
  secrets = [
    { name = "Gemini__ApiKey", value_from = "${module.secrets.secret_arn}:ApiKey::" },
    { name = "Gemini__Model", value_from = "${module.secrets.secret_arn}:Model::" },
  ]
  secrets_manager_arns = [module.secrets.secret_arn]

  create_task_role       = true
  task_role_policy_json  = data.aws_iam_policy_document.picture_service_task.json

  service_connect_namespace_arn  = module.ecs_cluster.service_connect_namespace_arn
  service_connect_discovery_name = local.picture_service_name

  load_balancer_target_group_arn = module.internal_lb.target_group_arns[local.picture_service_name]

  tags = local.common_tags
}

# ---- Web BFF on Lambda + API Gateway (task 9.1) ----
# VPC-attached so it can reach CatalogService/PictureService via the
# internal NLB above — see modules/bff-lambda's header comment for the IAM/
# networking/runtime decisions and modules/internal-lb's header comment for
# why this is an NLB hop rather than ECS Service Connect.
module "bff_lambda" {
  source = "../../modules/bff-lambda"

  project_name = var.project_name
  aws_region   = var.aws_region

  vpc_id             = module.networking.vpc_id
  vpc_cidr           = module.networking.vpc_cidr
  private_subnet_ids = module.networking.private_subnet_ids

  internal_lb_dns_name           = module.internal_lb.dns_name
  internal_lb_security_group_id  = module.internal_lb.security_group_id
  catalog_service_listener_port  = local.catalog_service_listener_port
  picture_service_listener_port  = local.picture_service_listener_port

  photos_bucket_name = module.photo_storage.bucket_name
  photos_bucket_arn  = module.photo_storage.bucket_arn

  memory_size             = var.bff_lambda_memory_size
  timeout_seconds          = var.bff_lambda_timeout_seconds
  log_retention_days       = var.bff_lambda_log_retention_days
  provisioned_concurrency  = var.bff_lambda_provisioned_concurrency

  tags = local.common_tags
}

# ---- WASM client static assets + CloudFront (task 9.2/9.3) ----
# Serves NinjagoScanner.Web.Client's static assets as the default cache
# behavior, and proxies the BFF's API Gateway endpoint under "/api/*" from
# the same distribution/domain — see modules/static-site's header comment
# for the full routing/CORS/SPA-fallback reasoning.
module "web_client" {
  source = "../../modules/static-site"

  project_name   = var.project_name
  aws_account_id = data.aws_caller_identity.current.account_id

  api_origin_domain_name = module.bff_lambda.api_domain_name

  price_class          = var.web_client_cloudfront_price_class
  domain_aliases        = var.web_client_domain_aliases
  acm_certificate_arn   = var.web_client_acm_certificate_arn

  tags = local.common_tags
}
