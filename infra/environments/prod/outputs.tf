output "photo_bucket_name" {
  value = module.photo_storage.bucket_name
}

# The old (PhotoId-only) table - kept only for the add-collection-data-isolation migration tool's
# --old-table argument and eventual decommissioning; PictureService no longer talks to it once
# Storage__SidecarTableName is switched to collection_sidecar_table_name below.
output "sidecar_table_name" {
  value = module.sidecar_table.table_name
}

output "collection_sidecar_table_name" {
  value = module.collection_sidecar_table.table_name
}

output "github_actions_plan_role_arn" {
  value = module.github_oidc.plan_role_arn
}

output "github_actions_deploy_role_arn" {
  value = module.github_oidc.deploy_role_arn
}

output "picture_service_iam_user_name" {
  value = module.picture_service_iam_user.user_name
}

output "picture_service_access_key_id" {
  value = module.picture_service_iam_user.access_key_id
}

output "picture_service_secret_access_key" {
  value     = module.picture_service_iam_user.secret_access_key
  sensitive = true
}
