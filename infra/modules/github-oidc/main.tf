# GitHub Actions OIDC federation: lets GitHub Actions workflows in this repo
# assume AWS IAM roles by presenting a short-lived OIDC token, so CI never
# stores long-lived AWS access keys as a repo secret.
#
# Two roles are created, split by trust condition:
#   - "plan"   role: assumable from workflow runs triggered either by a pull
#     request or by a push to the deploy branch — used for `terraform plan`
#     and other read-only CI checks (the build/test gate).
#   - "deploy" role: assumable *only* from workflow runs triggered by a push
#     to the deploy branch (main — see proposal.md: "deploys per-project on
#     push to main") — used for `terraform apply` and the per-project
#     deploy workflows.
# A pull request — including one from a fork — can therefore never assume
# the role with write access to real infrastructure; the worst it can do is
# read (via the plan role) what plan/build checks legitimately need to read.

# Fetched dynamically rather than hardcoded: GitHub's OIDC token-signing
# certificate chain has rotated CAs before (and did again since this module
# was first written), so a hardcoded thumbprint goes stale — and AWS's
# `aws_iam_openid_connect_provider` resource validates the value is a
# well-formed 40-character SHA1 hex digest even though it no longer uses it
# to verify GitHub's identity specifically. See:
# https://github.blog/changelog/2023-06-27-github-actions-update-on-oidc-integration-with-aws/
data "tls_certificate" "github_actions" {
  count = var.create_oidc_provider ? 1 : 0
  url   = "https://token.actions.githubusercontent.com"
}

resource "aws_iam_openid_connect_provider" "github_actions" {
  count = var.create_oidc_provider ? 1 : 0

  url            = "https://token.actions.githubusercontent.com"
  client_id_list = ["sts.amazonaws.com"]

  # data.tls_certificate orders its chain root-first, leaf-last (the
  # opposite of the order a TLS handshake presents certificates in) — index
  # 0 is the root CA AWS expects here, not the last index.
  thumbprint_list = [
    data.tls_certificate.github_actions[0].certificates[0].sha1_fingerprint,
  ]

  tags = var.tags
}

locals {
  oidc_provider_arn = var.create_oidc_provider ? aws_iam_openid_connect_provider.github_actions[0].arn : var.existing_oidc_provider_arn

  # GitHub's OIDC sub claim is NOT simply "repo:OWNER/REPO:..." — it's
  # "repo:OWNER@ownerId/REPO@repoId:...", including GitHub's own immutable
  # numeric IDs for the owner and repository (confirmed via CloudTrail
  # against a real rejected AssumeRoleWithWebIdentity call: the presented
  # identity was "repo:SebastianMartens@5823455/NinjagoScanner@1315298946:
  # environment:production", not "repo:SebastianMartens/NinjagoScanner:...").
  # Those IDs aren't knowable from Terraform config, so every sub pattern
  # below wildcards them with StringLike instead of hardcoding the plain
  # "owner/repo" form.
  repo_owner    = split("/", var.github_repo)[0]
  repo_name     = split("/", var.github_repo)[1]
  repo_sub_stem = "${local.repo_owner}*/${local.repo_name}*"
}

data "aws_iam_policy_document" "plan_trust" {
  statement {
    effect = "Allow"
    # sts:TagSession is required alongside sts:AssumeRoleWithWebIdentity
    # because aws-actions/configure-aws-credentials@v4 attaches session tags
    # (repository/workflow/actor/branch/commit, etc.) by default; without
    # this, AWS rejects the whole call as "not authorized" the moment any
    # tag is attached, not just the tagging part specifically.
    actions = ["sts:AssumeRoleWithWebIdentity", "sts:TagSession"]

    principals {
      type        = "Federated"
      identifiers = [local.oidc_provider_arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values = [
        "repo:${local.repo_sub_stem}:pull_request",
        "repo:${local.repo_sub_stem}:ref:refs/heads/${var.deploy_branch}",
      ]
    }
  }
}

data "aws_iam_policy_document" "deploy_trust" {
  statement {
    effect = "Allow"
    # sts:TagSession is required alongside sts:AssumeRoleWithWebIdentity
    # because aws-actions/configure-aws-credentials@v4 attaches session tags
    # (repository/workflow/actor/branch/commit, etc.) by default; without
    # this, AWS rejects the whole call as "not authorized" the moment any
    # tag is attached, not just the tagging part specifically.
    actions = ["sts:AssumeRoleWithWebIdentity", "sts:TagSession"]

    principals {
      type        = "Federated"
      identifiers = [local.oidc_provider_arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values = concat(
        ["repo:${local.repo_sub_stem}:ref:refs/heads/${var.deploy_branch}"],
        var.deploy_environment_name == "" ? [] : ["repo:${local.repo_sub_stem}:environment:${var.deploy_environment_name}"]
      )
    }
  }
}

resource "aws_iam_role" "plan" {
  name                 = var.plan_role_name
  assume_role_policy   = data.aws_iam_policy_document.plan_trust.json
  max_session_duration = 3600

  tags = var.tags
}

resource "aws_iam_role" "deploy" {
  name                 = var.deploy_role_name
  assume_role_policy   = data.aws_iam_policy_document.deploy_trust.json
  max_session_duration = 3600

  tags = var.tags
}

resource "aws_iam_role_policy" "plan" {
  for_each = { for index, json in var.plan_policy_jsons : tostring(index) => json }

  name   = "${var.plan_role_name}-policy-${each.key}"
  role   = aws_iam_role.plan.id
  policy = each.value
}

resource "aws_iam_role_policy" "deploy" {
  for_each = { for index, json in var.deploy_policy_jsons : tostring(index) => json }

  name   = "${var.deploy_role_name}-policy-${each.key}"
  role   = aws_iam_role.deploy.id
  policy = each.value
}
