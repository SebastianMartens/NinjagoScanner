variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "photo_bucket_arn" {
  description = "ARN of the S3 bucket holding card photos (modules/photo-storage's bucket_arn output)."
  type        = string
}

variable "sidecar_table_arn" {
  description = "ARN of the DynamoDB sidecar table (modules/sidecar-table's table_arn output)."
  type        = string
}

variable "tags" {
  type    = map(string)
  default = {}
}
