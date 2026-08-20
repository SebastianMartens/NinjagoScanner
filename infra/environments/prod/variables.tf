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

# ---- Networking (task 1.3) ----

variable "vpc_cidr" {
  type    = string
  default = "10.0.0.0/16"
}

variable "az_count" {
  type    = number
  default = 2
}

variable "single_nat_gateway" {
  description = "See infra/modules/networking's variable of the same name — cost vs. redundancy trade-off."
  type        = bool
  default     = true
}

# ---- Photo storage (task 2.1) ----

variable "photo_bucket_cors_origins" {
  description = <<-EOT
    Origins allowed to PUT/GET photos directly against S3. Defaults to "*" until manually
    tightened. Task 9.2/9.3 created the WASM client's actual CloudFront domain
    (module.web_client.distribution_domain_name), but this can't be wired to it automatically:
    module.bff_lambda needs module.photo_storage's bucket name/ARN (for its own S3 permissions and
    Storage__PhotosBucketName env var), and module.web_client needs module.bff_lambda's API
    Gateway domain — so photo_storage is upstream of web_client in the same apply's dependency
    graph, and having its CORS origin reference web_client's output back would be a real cycle.
    Tighten this by hand after the first apply: `terraform output` the actual CloudFront domain
    from module.web_client, set this variable to ["https://<that domain>"] in terraform.tfvars,
    and re-apply — the same "known only after first apply, set out of band" pattern this stack
    already uses for the Gemini secret's value (see README.md's Bootstrapping section).
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

# ---- CatalogService and PictureService on Fargate (task 5) ----

variable "catalog_service_image_tag" {
  description = "Image tag ECS pulls for CatalogService. Defaults to \"latest\", which does not exist in ECR until the first CI image push (task 10.2, not built yet) — until then this service's tasks simply fail to start, a normal and expected gap rather than a Terraform bug. Pin to a specific tag once CI is pushing real images."
  type        = string
  default     = "latest"
}

variable "picture_service_image_tag" {
  description = "Same as catalog_service_image_tag, for PictureService (task 10.3)."
  type        = string
  default     = "latest"
}

variable "catalog_service_cpu" {
  description = "Fargate task vCPU units (1024 = 1 vCPU). CatalogService serves an in-memory catalog with no external calls, so the smallest Fargate size is enough."
  type        = number
  default     = 256
}

variable "catalog_service_memory" {
  type    = number
  default = 512
}

variable "catalog_service_desired_count" {
  type    = number
  default = 1
}

variable "picture_service_cpu" {
  description = "Slightly larger than CatalogService's — PictureService handles image bytes and calls the Gemini API."
  type        = number
  default     = 512
}

variable "picture_service_memory" {
  type    = number
  default = 1024
}

variable "picture_service_desired_count" {
  type    = number
  default = 1
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

# ---- Web BFF on Lambda + API Gateway (task 9.1) ----

variable "bff_lambda_memory_size" {
  description = "See modules/bff-lambda's variable of the same name."
  type        = number
  default     = 512
}

variable "bff_lambda_timeout_seconds" {
  type    = number
  default = 29
}

variable "bff_lambda_log_retention_days" {
  type    = number
  default = 14
}

variable "bff_lambda_provisioned_concurrency" {
  description = <<-EOT
    0 (disabled) by default — see modules/bff-lambda's variable of the same name, design.md's
    Risks section, and infra/README.md's "Cold start / provisioned concurrency" note. Task 9.4
    calls for *measuring* real cold-start latency before deciding this; that measurement needs a
    live deployment this environment doesn't have. Recommendation: deploy first with this at 0,
    measure real p50/p99 cold-start latency against actual traffic patterns, and only raise this
    if a measured number proves unacceptable for a personal-scale app — design.md's own default
    assumption is that it won't be necessary.
  EOT
  type        = number
  default     = 0
}

# ---- WASM client static assets + CloudFront (task 9.2/9.3) ----

variable "web_client_cloudfront_price_class" {
  description = "See modules/static-site's variable of the same name."
  type        = string
  default     = "PriceClass_100"
}

variable "web_client_domain_aliases" {
  description = "Custom domain(s) for the WASM client's CloudFront distribution. Empty until task 11.4's DNS cutover."
  type        = list(string)
  default     = []
}

variable "web_client_acm_certificate_arn" {
  description = "ACM certificate (us-east-1 only, see modules/static-site) for web_client_domain_aliases. Unset until task 11.4."
  type        = string
  default     = null
}
