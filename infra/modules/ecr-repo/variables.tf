variable "repository_name" {
  type = string
}

variable "untagged_image_expiry_days" {
  type    = number
  default = 7
}

variable "max_tagged_image_count" {
  type    = number
  default = 10
}

variable "tags" {
  type    = map(string)
  default = {}
}
