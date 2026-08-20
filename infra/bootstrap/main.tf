# Run once per AWS account, before any other Terraform root in this repo:
#
#   cd infra/bootstrap
#   terraform init
#   terraform apply
#   terraform output state_backend_config
#
# See ../README.md for the full sequence, including how the printed output
# feeds into environments/prod/backend.hcl.

locals {
  tags = {
    Project   = var.project_name
    ManagedBy = "terraform"
    Component = "bootstrap"
  }
}

module "state_backend" {
  source = "../modules/state-backend"

  project_name = var.project_name
  tags         = local.tags
}
