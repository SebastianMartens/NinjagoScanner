output "state_bucket_name" {
  value = module.state_backend.state_bucket_name
}

output "state_bucket_arn" {
  value = module.state_backend.state_bucket_arn
}

output "lock_table_name" {
  value = module.state_backend.lock_table_name
}

output "lock_table_arn" {
  value = module.state_backend.lock_table_arn
}

output "state_backend_config" {
  description = "Paste these values into environments/<env>/backend.hcl (copied from backend.hcl.example)."
  value       = <<-EOT
    bucket       = "${module.state_backend.state_bucket_name}"
    key          = "${var.project_name}/<environment>/terraform.tfstate"
    region       = "${var.aws_region}"
    use_lockfile = true
    encrypt      = true
  EOT
}
