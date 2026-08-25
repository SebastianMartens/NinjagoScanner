locals {
  common_tags = {
    Project     = var.project_name
    Environment = var.environment
    ManagedBy   = "terraform"
  }
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

module "picture_service_iam_user" {
  source = "../../modules/iam-user"

  project_name      = var.project_name
  photo_bucket_arn  = module.photo_storage.bucket_arn
  sidecar_table_arn = module.sidecar_table.table_arn
  tags              = local.common_tags
}

module "github_oidc" {
  source = "../../modules/github-oidc"

  github_repo      = var.github_repo
  deploy_branch    = var.deploy_branch
  plan_role_name   = var.github_plan_role_name
  deploy_role_name = var.github_deploy_role_name
  # .github/workflows/terraform.yml's apply job declares `environment:
  # production`, which changes its OIDC sub claim's format — see
  # modules/github-oidc's deploy_environment_name variable for why this
  # needs to be listed explicitly.
  deploy_environment_name = "production"
  plan_policy_jsons       = [data.aws_iam_policy_document.plan.json]
  deploy_policy_jsons     = [data.aws_iam_policy_document.deploy_core.json]
  tags                    = local.common_tags
}
