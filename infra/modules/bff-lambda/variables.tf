variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "function_name" {
  description = "Short name for the BFF Lambda function — used to name the function itself, its execution role, its security group, its CloudWatch log group, and (via environments/prod/iam-policies.tf) the deterministic IAM ARNs the deploy role's policy is scoped to."
  type        = string
  default     = "bff"
}

variable "aws_region" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "vpc_cidr" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

# ---- Reaching CatalogService/PictureService (see modules/internal-lb) ----

variable "internal_lb_dns_name" {
  description = "Internal-only DNS name of the shared NLB fronting CatalogService/PictureService (modules/internal-lb). The BFF's gRPC clients are pointed at this, not at Service Connect — see that module's header comment for why."
  type        = string
}

variable "internal_lb_security_group_id" {
  description = "Security group of the internal NLB (modules/internal-lb) — this function's own security group is given egress to it on the listener ports below."
  type        = string
}

variable "catalog_service_listener_port" {
  type = number
}

variable "picture_service_listener_port" {
  type = number
}

# ---- The BFF's own AWS access (S3 presigned URLs) ----

variable "photos_bucket_name" {
  description = "S3 bucket the BFF issues presigned upload/download URLs against (modules/photo-storage) — must match PictureService's own bucket (see BffConfig.ResolvePhotosBucketName)."
  type        = string
}

variable "photos_bucket_arn" {
  type = string
}

# ---- Sizing / runtime ----

variable "memory_size" {
  description = "Lambda memory in MB. Also proportionally scales allotted CPU. Kept modest — the BFF does no CPU-heavy work, only JSON (de)serialization and gRPC calls it awaits on."
  type        = number
  default     = 512
}

variable "timeout_seconds" {
  description = "Lambda timeout. API Gateway HTTP API's own integration timeout is a hard 30s ceiling for any synchronous request regardless of this value, so this is kept just under that rather than Lambda's own much higher 900s max."
  type        = number
  default     = 29
}

variable "log_retention_days" {
  type    = number
  default = 14
}

variable "provisioned_concurrency" {
  description = <<-EOT
    Number of provisioned-concurrency instances to keep warm. 0 (default) disables it entirely —
    no aws_lambda_provisioned_concurrency_config resource is created. See design.md's Risks
    section and infra/README.md's "Cold start / provisioned concurrency" note: task 9.4 calls for
    *measuring* real cold-start latency before deciding this, which needs a live deployment that
    doesn't exist yet from this environment. Leaving this at 0 is the explicit recommendation
    until real numbers exist post-deployment — flip it only once a measured cold-start actually
    proves unacceptable for this app's personal-scale traffic pattern (design.md's default
    assumption is that it won't).
  EOT
  type        = number
  default     = 0

  validation {
    condition     = var.provisioned_concurrency >= 0
    error_message = "provisioned_concurrency must be >= 0."
  }
}

variable "tags" {
  type    = map(string)
  default = {}
}
