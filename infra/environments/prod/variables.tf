variable "aws_region" {
  description = "AWS region for all resources. eu-central-1 (Frankfurt) by default — closest AWS region to the app's German-language user base (see CLAUDE.md: default sidecar language is \"de\")."
  type        = string
  default     = "eu-central-1"
}

variable "project_name" {
  description = "Short project slug used to name/prefix/tag resources."
  type        = string
  default     = "ninjago-scanner"
}

variable "environment" {
  type    = string
  default = "prod"
}

# ---- Photo storage (task 2.1) ----

variable "photo_bucket_cors_origins" {
  description = <<-EOT
    Origins allowed to PUT/GET photos directly against S3. Defaults to "*" until manually
    tightened to the app's actual origin(s) once compute (Fly.io follow-up change) is deployed.
  EOT
  type        = list(string)
  default     = ["*"]
}

# ---- Sidecar table (task 2.2) ----

variable "sidecar_table_deletion_protection" {
  type    = bool
  default = true
}

# ---- GitHub OIDC (task 1.2) ----

variable "github_repo" {
  description = "GitHub repo GitHub Actions runs from, as \"owner/name\"."
  type        = string
  default     = "SebastianMartens/NinjagoScanner"
}

variable "deploy_branch" {
  description = "Branch that write/deploy actions (terraform apply, service deploys) are restricted to."
  type        = string
  default     = "main"
}

variable "github_plan_role_name" {
  type    = string
  default = "ninjago-scanner-github-actions-plan"
}

variable "github_deploy_role_name" {
  type    = string
  default = "ninjago-scanner-github-actions-deploy"
}

# ---- Terraform state backend access (created by infra/bootstrap) ----
# These have no defaults on purpose — copy them from
# `terraform output` in infra/bootstrap into terraform.tfvars (gitignored;
# see terraform.tfvars.example).

variable "state_bucket_arn" {
  description = "ARN of the Terraform state bucket created by infra/bootstrap."
  type        = string
}

variable "state_lock_table_arn" {
  description = "ARN of the Terraform state lock table created by infra/bootstrap."
  type        = string
}
