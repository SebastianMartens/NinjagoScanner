variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "vpc_id" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "ingress_cidr" {
  description = <<-EOT
    CIDR allowed to reach the listener ports below. Defaults to the whole VPC.

    Task 9 note: this was originally left VPC-wide with a TODO to tighten it to the BFF Lambda's
    security group specifically once that SG existed (modules/bff-lambda). Deliberately not done:
    doing so would require this module to take the Lambda SG as an input, while modules/bff-lambda
    itself needs this module's dns_name/security_group_id as inputs (to build its
    CatalogService/PictureService addresses and scope its own egress) — wiring both directions
    creates a real Terraform module dependency cycle, not just an ordering inconvenience. Since
    the NLB is already unreachable from outside the VPC (`internal = true`, no public route) and
    only Fargate tasks + the BFF Lambda ever run in these private subnets, VPC-wide CIDR scoping
    here is an accepted trade-off rather than an oversight — the meaningful boundary (no path from
    the public internet) is already enforced. The BFF Lambda's own security group (see
    modules/bff-lambda) *is* scoped tightly in the other direction: its egress only reaches this
    module's security_group_id on the two listener ports, nothing else.
  EOT
  type        = string
}

variable "targets" {
  description = "One entry per backend service fronted by this shared internal NLB."
  type = list(object({
    name              = string
    listener_port     = number
    target_port       = number
    health_check_path = string
    health_check_port = number
  }))
}

variable "tags" {
  type    = map(string)
  default = {}
}
