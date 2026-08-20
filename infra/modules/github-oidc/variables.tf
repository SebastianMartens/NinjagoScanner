variable "github_repo" {
  description = "GitHub repo allowed to assume these roles, as \"owner/name\"."
  type        = string
}

variable "deploy_branch" {
  description = "Branch that write/deploy actions are restricted to."
  type        = string
  default     = "main"
}

variable "create_oidc_provider" {
  description = "Whether to create the GitHub Actions OIDC provider. Set to false and supply existing_oidc_provider_arn if one already exists in this AWS account (an account can only have one provider per issuer URL — this matters if a second stack in the same account also wants GitHub OIDC federation)."
  type        = bool
  default     = true
}

variable "existing_oidc_provider_arn" {
  description = "ARN of an existing GitHub OIDC provider, used only when create_oidc_provider = false."
  type        = string
  default     = ""
}

variable "plan_role_name" {
  type    = string
  default = "ninjago-scanner-github-actions-plan"
}

variable "deploy_role_name" {
  type    = string
  default = "ninjago-scanner-github-actions-deploy"
}

variable "plan_policy_jsons" {
  description = "IAM policy documents (JSON) attached to the plan role, one inline aws_iam_role_policy per entry. A list rather than one merged document because a single inline role policy is capped at 10,240 bytes by AWS, and this stack's combined permissions (networking, storage, ECS/Fargate, Lambda/API Gateway/CloudFront, ...) already exceed that as one document."
  type        = list(string)
}

variable "deploy_policy_jsons" {
  description = "IAM policy documents (JSON) attached to the deploy role, one inline aws_iam_role_policy per entry. See plan_policy_jsons for why this is a list, not one document."
  type        = list(string)
}

variable "tags" {
  type    = map(string)
  default = {}
}
