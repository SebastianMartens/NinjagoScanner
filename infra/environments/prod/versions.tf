terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    # Only used by modules/bff-lambda to generate a placeholder Lambda
    # deployment package on the first apply (task 9.1) — see that module's
    # header comment for why aws_lambda_function needs a real zip to exist
    # at creation time, and why Terraform doesn't own the function's real
    # code after that (task 10.4's CI workflow does).
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.4"
    }
    # Used by modules/github-oidc to fetch GitHub's OIDC token-signing
    # certificate thumbprint dynamically instead of hardcoding it — see
    # that module's main.tf for why (GitHub's intermediate CA rotates over
    # time; a hardcoded value goes stale and, worse, silently wrong).
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  backend "s3" {}
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = local.common_tags
  }
}
