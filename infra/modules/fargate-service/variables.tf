variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "service_name" {
  description = "Short name for this service, e.g. \"catalog-service\" — used to name/prefix the task definition, ECS service, log group, IAM roles, and Service Connect's discovery name."
  type        = string
}

variable "cluster_id" {
  type = string
}

variable "vpc_id" {
  type = string
}

variable "vpc_cidr" {
  description = "Used to scope the task security group's ingress rule to \"inside this VPC\" (Service Connect peers + the internal NLB) — see main.tf's security group comment."
  type        = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "ecr_repository_arn" {
  description = "ARN of this service's ECR repository (modules/ecr-repo) — the execution role is scoped to pull only from it."
  type        = string
}

variable "container_image" {
  description = "Full image URI, e.g. \"<account>.dkr.ecr.eu-central-1.amazonaws.com/ninjago-scanner-catalog-service:latest\"."
  type        = string
}

variable "container_port" {
  type    = number
  default = 8080
}

variable "health_check_port" {
  description = <<-EOT
    Optional second container port for a plain HTTP/1.1 liveness probe, distinct from
    container_port. CatalogService/PictureService serve gRPC (HTTP/2) on container_port;
    Kestrel can't multiplex HTTP/1.1 and HTTP/2 on one unencrypted port (ALPN needs TLS
    to negotiate), so the NLB's HTTP health check (modules/internal-lb) needs its own
    port when container_port is HTTP/2-only. Pass null to skip it (no extra port mapping
    or security group ingress) for services that don't need a health check.
  EOT
  type        = number
  default     = null
}

variable "cpu" {
  type    = number
  default = 256
}

variable "memory" {
  type    = number
  default = 512
}

variable "desired_count" {
  type    = number
  default = 1
}

variable "environment_variables" {
  description = "Plain (non-secret) container environment variables."
  type        = map(string)
  default     = {}
}

variable "secrets" {
  description = "Container environment variables sourced from Secrets Manager at task startup, resolved via the task *execution* role (see main.tf's header comment on why this isn't the task role). Each value_from can address a whole secret or a specific JSON key within one (\"<secret-arn>:<json-key>::\")."
  type = list(object({
    name       = string
    value_from = string
  }))
  default = []
}

variable "secrets_manager_arns" {
  description = "Secret ARNs (not the \"arn:...:jsonkey::\" form — the plain secret ARN) the execution role needs secretsmanager:GetSecretValue on, to resolve `secrets` above."
  type        = list(string)
  default     = []
}

variable "create_task_role" {
  description = "Whether to create a task role at all for this service. Kept as its own plain boolean, statically known at plan time, rather than inferred from task_role_policy_json == null — that policy document is typically built from other not-yet-created resources' attributes (e.g. a bucket/table ARN), which makes its value unknown until apply and unusable as a count/for_each condition (Terraform requires those to be known at plan time)."
  type        = bool
  default     = false
}

variable "task_role_policy_json" {
  description = "IAM policy document (JSON) granting this service's own application code (not the execution role) direct AWS access — e.g. PictureService's S3/DynamoDB access. Only used when create_task_role is true; ignored (and fine to leave null) otherwise."
  type        = string
  default     = null
}

variable "log_retention_days" {
  type    = number
  default = 14
}

variable "service_connect_namespace_arn" {
  type = string
}

variable "service_connect_discovery_name" {
  description = "The internal DNS name other Service-Connect-enabled ECS tasks in the same namespace use to reach this service, e.g. \"catalog-service\" resolving (from other ECS tasks only — see modules/ecs-cluster) to \"http://catalog-service:8080\"."
  type        = string
}

variable "load_balancer_target_group_arn" {
  description = "If set, registers this ECS service with this target group (the shared internal NLB — modules/internal-lb) so the BFF Lambda can reach it directly. Pass null for a service that only needs to be reachable via Service Connect."
  type        = string
  default     = null
}

variable "tags" {
  type    = map(string)
  default = {}
}
