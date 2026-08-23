output "photo_bucket_name" {
  value = module.photo_storage.bucket_name
}

output "sidecar_table_name" {
  value = module.sidecar_table.table_name
}

output "github_actions_plan_role_arn" {
  value = module.github_oidc.plan_role_arn
}

output "github_actions_deploy_role_arn" {
  value = module.github_oidc.deploy_role_arn
}
