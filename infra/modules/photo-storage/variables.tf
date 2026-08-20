variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "cors_allowed_origins" {
  description = "Origins allowed to PUT/GET photos directly against S3 — should be the WASM client's actual CloudFront domain (modules/static-site, task 9.2) in production. Defaults to \"*\"; environments/prod/main.tf can't wire this to that domain automatically without a real module dependency cycle (see environments/prod/variables.tf's photo_bucket_cors_origins for the full reasoning) — tighten by hand after the first apply."
  type        = list(string)
  default     = ["*"]
}

variable "tags" {
  type    = map(string)
  default = {}
}
