variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "aws_region" {
  type = string
}

variable "vpc_cidr" {
  type    = string
  default = "10.0.0.0/16"
}

variable "az_count" {
  description = "Number of availability zones to spread subnets across (min 2 — Fargate services behind an ALB, and the ALB itself, both expect multi-AZ subnets)."
  type        = number
  default     = 2

  validation {
    condition     = var.az_count >= 2
    error_message = "az_count must be at least 2 — Fargate services and the ALB both expect multi-AZ subnets."
  }
}

variable "single_nat_gateway" {
  description = "true = one NAT Gateway (in the first AZ) shared by all private subnets — cheaper, but a single point of failure for outbound egress if that AZ has an issue. false = one NAT Gateway per AZ, matching the redundancy of the rest of the multi-AZ setup at roughly 2x the NAT cost. This is a personal-scale portfolio project, so it defaults to the cheaper option; flip this for anything closer to a real production workload."
  type        = bool
  default     = true
}

variable "tags" {
  type    = map(string)
  default = {}
}
