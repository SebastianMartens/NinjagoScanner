output "function_name" {
  value = aws_lambda_function.bff.function_name
}

output "function_arn" {
  value = aws_lambda_function.bff.arn
}

output "execution_role_arn" {
  value = aws_iam_role.lambda.arn
}

output "security_group_id" {
  value = aws_security_group.lambda.id
}

output "api_id" {
  value = aws_apigatewayv2_api.bff.id
}

# The API Gateway's own default execute-api domain — no custom domain name
# is provisioned here (see main.tf). This is what modules/static-site's
# CloudFront distribution uses as its second (BFF) origin for the "/api/*"
# path pattern (task 9.3).
output "api_domain_name" {
  value = replace(aws_apigatewayv2_api.bff.api_endpoint, "https://", "")
}

output "invoke_url" {
  description = "Full public HTTPS URL for this API Gateway stage — reachable directly (bypassing CloudFront), useful for smoke-testing the BFF in isolation once deployed."
  value       = aws_apigatewayv2_stage.default.invoke_url
}
