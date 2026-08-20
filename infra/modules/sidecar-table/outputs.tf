output "table_name" {
  value = aws_dynamodb_table.sidecars.name
}

output "table_arn" {
  value = aws_dynamodb_table.sidecars.arn
}
