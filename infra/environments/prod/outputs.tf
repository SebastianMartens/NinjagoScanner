output "vpc_id" {
  value = module.networking.vpc_id
}

output "public_subnet_ids" {
  value = module.networking.public_subnet_ids
}

output "private_subnet_ids" {
  value = module.networking.private_subnet_ids
}

output "photo_bucket_name" {
  value = module.photo_storage.bucket_name
}

output "sidecar_table_name" {
  value = module.sidecar_table.table_name
}

output "gemini_secret_name" {
  value = module.secrets.secret_name
}

output "github_actions_plan_role_arn" {
  value = module.github_oidc.plan_role_arn
}

output "github_actions_deploy_role_arn" {
  value = module.github_oidc.deploy_role_arn
}

output "ecr_catalog_service_repository_url" {
  value = module.ecr_catalog_service.repository_url
}

output "ecr_picture_service_repository_url" {
  value = module.ecr_picture_service.repository_url
}

output "ecs_cluster_name" {
  value = module.ecs_cluster.cluster_name
}

output "internal_lb_dns_name" {
  description = "Internal-only DNS name fronting CatalogService/PictureService for the BFF Lambda (task 5.4/9). Not resolvable outside the VPC."
  value       = module.internal_lb.dns_name
}

output "bff_function_name" {
  description = "Used by task 10.4's deploy workflow: `aws lambda update-function-code --function-name <this>`."
  value       = module.bff_lambda.function_name
}

output "bff_api_invoke_url" {
  description = "Direct API Gateway URL (bypassing CloudFront) — for smoke-testing the BFF in isolation."
  value       = module.bff_lambda.invoke_url
}

output "web_client_bucket_name" {
  description = "Used by task 10.5's deploy workflow: `aws s3 sync <publish output> s3://<this>`."
  value       = module.web_client.bucket_name
}

output "web_client_distribution_id" {
  description = "Used by task 10.5's deploy workflow: `aws cloudfront create-invalidation --distribution-id <this>`."
  value       = module.web_client.distribution_id
}

output "web_client_distribution_domain_name" {
  description = "The app's actual public entry point (until task 11.4 sets a custom domain)."
  value       = module.web_client.distribution_domain_name
}
