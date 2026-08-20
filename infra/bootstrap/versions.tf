terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  # Deliberately no `backend` block here: this config creates the remote
  # state backend that every other root module (environments/*) uses, so it
  # has to keep its own state locally rather than depend on a backend it
  # hasn't created yet. Its state is tiny and changes rarely — back up
  # `terraform.tfstate` after applying (it is gitignored, like all state).
}

provider "aws" {
  region = var.aws_region
}
