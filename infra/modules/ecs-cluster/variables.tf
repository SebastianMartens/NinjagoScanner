variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "container_insights_enabled" {
  description = "CloudWatch Container Insights adds per-task/service metrics at extra cost. Off by default for this personal-scale project; flip on for real observability needs."
  type        = bool
  default     = false
}

variable "tags" {
  type    = map(string)
  default = {}
}
