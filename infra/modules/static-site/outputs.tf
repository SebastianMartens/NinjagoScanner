output "bucket_name" {
  value = aws_s3_bucket.web_client.bucket
}

output "bucket_arn" {
  value = aws_s3_bucket.web_client.arn
}

output "distribution_id" {
  description = "Used by the future WASM client deploy workflow (task 10.5) to invalidate the cache after each `aws s3 sync`."
  value       = aws_cloudfront_distribution.web.id
}

output "distribution_arn" {
  value = aws_cloudfront_distribution.web.arn
}

output "distribution_domain_name" {
  description = "Public *.cloudfront.net domain (or var.domain_aliases, once task 11.4 sets one) — the app's actual public entry point."
  value       = aws_cloudfront_distribution.web.domain_name
}
