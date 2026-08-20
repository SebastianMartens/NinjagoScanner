variable "project_name" {
  description = "Short project slug used to name/prefix resources."
  type        = string
  default     = "ninjago-scanner"
}

variable "tags" {
  description = "Common resource tags."
  type        = map(string)
  default     = {}
}
