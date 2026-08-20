variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "environment" {
  type    = string
  default = "prod"
}

variable "recovery_window_in_days" {
  description = "Days a deleted secret stays recoverable before permanent deletion. 0 = delete immediately (useful for a throwaway dev stack, not recommended here)."
  type        = number
  default     = 7
}

variable "tags" {
  type    = map(string)
  default = {}
}
