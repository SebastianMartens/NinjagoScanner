output "table_name" {
  value = aws_dynamodb_table.collection_sidecars.name
}

output "table_arn" {
  value = aws_dynamodb_table.collection_sidecars.arn
}
