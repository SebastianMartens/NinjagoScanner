variable "aws_region" {
  description = "AWS region to bootstrap the Terraform state backend in. eu-central-1 (Frankfurt) by default, matching every other stack in this repo."
  type        = string
  default     = "eu-central-1"
}

variable "project_name" {
  description = "Short project slug used to name/prefix resources."
  type        = string
  default     = "ninjago-scanner"
}
