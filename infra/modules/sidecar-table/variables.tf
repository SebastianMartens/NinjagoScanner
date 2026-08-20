variable "project_name" {
  type    = string
  default = "ninjago-scanner"
}

variable "deletion_protection_enabled" {
  description = "Prevents accidental `terraform destroy`/console deletion of the table. Defaults on since this is the durable record of every scanned card; turn off deliberately (e.g. for a throwaway dev copy of this stack) rather than as a blanket default."
  type        = bool
  default     = true
}

variable "tags" {
  type    = map(string)
  default = {}
}
