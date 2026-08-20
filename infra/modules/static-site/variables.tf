variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "aws_account_id" {
  description = "Used to suffix the bucket name for global uniqueness, matching modules/photo-storage's naming convention."
  type        = string
}

variable "api_origin_domain_name" {
  description = "The BFF's API Gateway execute-api domain (no scheme, no trailing slash — see modules/bff-lambda's api_domain_name output), used as this distribution's second origin for the \"/api/*\" path pattern (task 9.3)."
  type        = string
}

variable "price_class" {
  description = "CloudFront price class. PriceClass_100 (cheapest — North America + Europe edge locations only) matches this app's actual audience (see infra/README.md: eu-central-1 chosen for the German-language user base) and this stack's existing cost-consciousness."
  type        = string
  default     = "PriceClass_100"
}

variable "domain_aliases" {
  description = "Custom domain name(s) for the distribution (CNAMEs), e.g. [\"ninjago.example.com\"]. Empty by default — the distribution is reachable at its own *.cloudfront.net domain until task 11.4's DNS cutover supplies a real domain + var.acm_certificate_arn."
  type        = list(string)
  default     = []
}

variable "acm_certificate_arn" {
  description = "ACM certificate ARN for domain_aliases, required (and only used) once domain_aliases is non-empty. Must be a certificate in us-east-1 regardless of var.aws_region — a CloudFront-specific ACM requirement. Left unset (null) until task 11.4."
  type        = string
  default     = null

  validation {
    condition     = var.acm_certificate_arn == null || can(regex("^arn:aws:acm:us-east-1:", var.acm_certificate_arn))
    error_message = "acm_certificate_arn must be an ACM certificate in us-east-1 — CloudFront only accepts certificates from that region regardless of where the rest of this stack runs."
  }
}

variable "tags" {
  type    = map(string)
  default = {}
}
