output "user_name" {
  value = aws_iam_user.picture_service.name
}

output "access_key_id" {
  value = aws_iam_access_key.picture_service.id
}

output "secret_access_key" {
  value     = aws_iam_access_key.picture_service.secret
  sensitive = true
}
