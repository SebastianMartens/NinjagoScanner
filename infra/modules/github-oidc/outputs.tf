output "plan_role_arn" {
  value = aws_iam_role.plan.arn
}

output "deploy_role_arn" {
  value = aws_iam_role.deploy.arn
}

output "oidc_provider_arn" {
  value = local.oidc_provider_arn
}
