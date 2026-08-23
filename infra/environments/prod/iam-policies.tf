# IAM policy documents attached to the GitHub Actions OIDC roles (see
# ../../modules/github-oidc and README.md for the plan/deploy role split).
#
# Scope: what Terraform needs to manage the resources *this* stack declares
# (photo storage, the sidecar table, the OIDC provider/roles) plus read/write
# access to this stack's own remote state (the S3 bucket + DynamoDB lock
# table created by infra/bootstrap). Compute (VPC/ECS/Fargate/Lambda/
# CloudFront/ECR/Secrets Manager) was torn down by the aws-compute-teardown
# change — see that change's proposal for why — and its permission
# statements were removed here along with it, not just the resources they
# pointed at.
#
# The deploy role is also granted rights to manage its own IAM role/policy
# resources and the OIDC provider (scoped to those exact resource names,
# never iam:* broadly) — Terraform manages the OIDC provider and both roles
# as regular resources, so the role that runs `terraform apply` needs
# permission to update itself. This is a standard but self-referential
# bootstrapping pattern: the *first* `terraform apply` for this environment
# has to be run by a human with their own sufficiently-privileged AWS
# credentials (see README.md "First apply"); after that, CI can take over
# using the deploy role.

data "aws_caller_identity" "current" {}

locals {
  account_id = data.aws_caller_identity.current.account_id

  # Deterministic ARNs for resources this policy needs to reference before
  # (or while) they're created by the same apply that grants access to them.
  # IAM role and OIDC provider ARNs are fully deterministic from the names
  # we choose (no AWS-assigned random suffix), so these are computed from
  # local values rather than from module outputs — using a module output
  # here would make the deploy role's own policy depend on the very
  # resources its trust relationship is supposed to authorize creating.
  plan_role_arn      = "arn:aws:iam::${local.account_id}:role/${var.github_plan_role_name}"
  deploy_role_arn    = "arn:aws:iam::${local.account_id}:role/${var.github_deploy_role_name}"
  oidc_provider_arn  = "arn:aws:iam::${local.account_id}:oidc-provider/token.actions.githubusercontent.com"
  self_iam_role_arns = [local.plan_role_arn, local.deploy_role_arn]

  # The deploy role's own permission documents (modules/github-oidc's
  # deploy_policy_jsons) are attached as customer-managed policies, not
  # inline ones — see that variable's description for why. Their names are
  # deterministic ("${deploy_role_name}-policy-${index}"), so — same
  # reasoning as the role/OIDC ARNs above — this pattern can be computed
  # before those policies exist.
  deploy_managed_policy_arn_pattern = "arn:aws:iam::${local.account_id}:policy/${var.github_deploy_role_name}-policy-*"
}

# ---- Terraform backend access (state bucket + lock table) ----
# Both roles need this: `terraform plan` reads state and takes/releases the
# lock exactly like `apply` does, even though it changes nothing in AWS.
data "aws_iam_policy_document" "backend_access" {
  statement {
    sid       = "StateObjectReadWrite"
    effect    = "Allow"
    actions   = ["s3:GetObject", "s3:PutObject"]
    resources = ["${var.state_bucket_arn}/${var.project_name}/${var.environment}/*"]
  }

  statement {
    sid       = "StateBucketList"
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = [var.state_bucket_arn]
  }

  statement {
    sid       = "StateLock"
    effect    = "Allow"
    actions   = ["dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:DeleteItem"]
    resources = [var.state_lock_table_arn]
  }
}

# ---- Resource management (this stack's actual infrastructure) ----
data "aws_iam_policy_document" "manage_resources_core" {
  statement {
    sid    = "PhotoBucket"
    effect = "Allow"
    actions = [
      "s3:CreateBucket", "s3:DeleteBucket",
      "s3:GetBucket*", "s3:PutBucket*",
      "s3:GetLifecycleConfiguration", "s3:PutLifecycleConfiguration",
      "s3:GetEncryptionConfiguration", "s3:PutEncryptionConfiguration",
      "s3:GetBucketPolicy", "s3:PutBucketPolicy", "s3:DeleteBucketPolicy",
      "s3:PutBucketVersioning", "s3:GetBucketVersioning",
      "s3:PutBucketCORS", "s3:GetBucketCORS",
      "s3:PutBucketPublicAccessBlock", "s3:GetBucketPublicAccessBlock",
      "s3:PutBucketTagging", "s3:GetBucketTagging",
    ]
    resources = [module.photo_storage.bucket_arn]
  }

  statement {
    sid    = "SidecarTable"
    effect = "Allow"
    actions = [
      "dynamodb:CreateTable", "dynamodb:DeleteTable", "dynamodb:UpdateTable",
      "dynamodb:DescribeTable", "dynamodb:TagResource", "dynamodb:UntagResource",
      "dynamodb:ListTagsOfResource", "dynamodb:UpdateContinuousBackups",
      "dynamodb:DescribeContinuousBackups", "dynamodb:UpdateTimeToLive",
      "dynamodb:DescribeTimeToLive",
    ]
    resources = [module.sidecar_table.table_arn, "${module.sidecar_table.table_arn}/index/*"]
  }
}

# ---- Self-managed IAM (the deploy/plan roles, their policies, the OIDC
# provider) ----
# Split from manage_resources_core because these statements aren't about
# this stack's application resources — they're the deploy role provisioning
# its own IAM footprint (see the file header's bootstrapping-pattern note).
data "aws_iam_policy_document" "self_managed_iam" {
  statement {
    sid    = "SelfManagedIamRoles"
    effect = "Allow"
    actions = [
      "iam:GetRole", "iam:CreateRole", "iam:DeleteRole", "iam:UpdateRole",
      # PutRolePolicy/DeleteRolePolicy/GetRolePolicy/ListRolePolicies are for
      # the plan role, which still uses an inline policy (see
      # deploy_policy_jsons's description for why the deploy role doesn't
      # anymore) — harmless to also grant on the deploy role's own ARN.
      "iam:GetRolePolicy", "iam:PutRolePolicy", "iam:DeleteRolePolicy",
      "iam:TagRole", "iam:UntagRole", "iam:ListRolePolicies",
      "iam:UpdateAssumeRolePolicy",
      "iam:AttachRolePolicy", "iam:DetachRolePolicy", "iam:ListAttachedRolePolicies",
    ]
    resources = local.self_iam_role_arns
  }

  # The deploy role's own permission documents are customer-managed
  # policies (see SelfManagedIamRoles above), so — unlike the plan role's
  # inline one — it needs rights to manage the policy objects themselves,
  # not just attach/detach them to its own role.
  statement {
    sid    = "SelfManagedPolicies"
    effect = "Allow"
    actions = [
      "iam:CreatePolicy", "iam:DeletePolicy", "iam:GetPolicy",
      "iam:GetPolicyVersion", "iam:ListPolicyVersions",
      "iam:CreatePolicyVersion", "iam:DeletePolicyVersion",
      "iam:TagPolicy", "iam:UntagPolicy",
    ]
    resources = [local.deploy_managed_policy_arn_pattern]
  }

  statement {
    sid    = "SelfManagedOidcProvider"
    effect = "Allow"
    actions = [
      "iam:CreateOpenIDConnectProvider", "iam:DeleteOpenIDConnectProvider",
      "iam:GetOpenIDConnectProvider", "iam:UpdateOpenIDConnectProviderThumbprint",
      "iam:TagOpenIDConnectProvider", "iam:UntagOpenIDConnectProvider",
      "iam:AddClientIDToOpenIDConnectProvider",
    ]
    resources = [local.oidc_provider_arn]
  }

  statement {
    sid       = "CallerIdentity"
    effect    = "Allow"
    actions   = ["sts:GetCallerIdentity"]
    resources = ["*"]
  }
}

data "aws_iam_policy_document" "deploy_core" {
  source_policy_documents = [
    data.aws_iam_policy_document.backend_access.json,
    data.aws_iam_policy_document.manage_resources_core.json,
    data.aws_iam_policy_document.self_managed_iam.json,
  ]
}

# ---- Read-only variant for the plan role ----
# `terraform plan` needs to *read* every resource type apply can write, but
# never needs the write verbs — same resource scoping as manage_resources,
# with each statement's actions swapped for their Describe/Get/List
# equivalents.
data "aws_iam_policy_document" "plan_only" {
  statement {
    sid    = "StorageRead"
    effect = "Allow"
    actions = [
      "s3:GetBucket*", "s3:GetLifecycleConfiguration", "s3:GetEncryptionConfiguration",
      "s3:GetBucketPolicy", "s3:GetBucketVersioning", "s3:GetBucketCORS",
      "s3:GetBucketPublicAccessBlock", "s3:GetBucketTagging",
      "dynamodb:DescribeTable", "dynamodb:ListTagsOfResource",
      "dynamodb:DescribeContinuousBackups", "dynamodb:DescribeTimeToLive",
    ]
    resources = [
      module.photo_storage.bucket_arn,
      module.sidecar_table.table_arn,
      "${module.sidecar_table.table_arn}/index/*",
    ]
  }

  statement {
    sid    = "SelfManagedIamRolesRead"
    effect = "Allow"
    actions = [
      "iam:GetRole", "iam:GetRolePolicy", "iam:ListRolePolicies",
      # Read equivalents for the deploy role's customer-managed policies
      # (see SelfManagedPolicies above) — `terraform plan` needs these to
      # refresh those resources too.
      "iam:ListAttachedRolePolicies", "iam:GetPolicy", "iam:GetPolicyVersion", "iam:ListPolicyVersions",
    ]
    resources = concat(local.self_iam_role_arns, [local.deploy_managed_policy_arn_pattern])
  }

  statement {
    sid       = "SelfManagedOidcProviderRead"
    effect    = "Allow"
    actions   = ["iam:GetOpenIDConnectProvider"]
    resources = [local.oidc_provider_arn]
  }

  statement {
    sid       = "CallerIdentity"
    effect    = "Allow"
    actions   = ["sts:GetCallerIdentity"]
    resources = ["*"]
  }
}

data "aws_iam_policy_document" "plan" {
  source_policy_documents = [
    data.aws_iam_policy_document.backend_access.json,
    data.aws_iam_policy_document.plan_only.json,
  ]
}
