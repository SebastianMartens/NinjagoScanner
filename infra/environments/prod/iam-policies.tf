# IAM policy documents attached to the GitHub Actions OIDC roles (see
# ../../modules/github-oidc and README.md for the plan/deploy role split).
#
# Scope: what Terraform needs to manage the resources *this* stack declares
# (networking, photo storage, sidecar table, the Gemini secret, the OIDC
# provider/roles, ECR/ECS Fargate/Service Connect/the internal NLB fronting
# CatalogService and PictureService (task 5), and — as of task 9 — the Web
# BFF's Lambda function/API Gateway and the WASM client's S3 bucket/
# CloudFront distribution) plus read/write access to this stack's own
# remote state (the S3 bucket + DynamoDB lock table created by
# infra/bootstrap). ACM (for a custom domain, task 11.4) is the one thing
# still not included below — var.web_client_domain_aliases/
# acm_certificate_arn are unused (empty/null) until then, so no ACM
# permissions are needed yet.
#
# Task 5's statements below double as what the future CI deploy workflows
# (task 10.2/10.3: image -> ECR -> `ecs update-service`, not built yet)
# will need at runtime, since they assume this same `deploy` role (see
# README.md's "Two GitHub Actions IAM roles" section) — ecr:PutImage/
# ecs:UpdateService etc. are already granted below, not added separately.
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
#
# EC2 VPC-family resources (aws_vpc, aws_subnet, aws_route_table, ...) don't
# support resource-level IAM permissions the way S3/DynamoDB do — most of
# their identifiers are AWS-assigned at creation time, not chosen up front,
# so IAM can't scope a statement to "the VPC this stack will create" before
# it exists. Those statements are therefore scoped by action (verb)
# allow-list only, with resources = "*" — an accepted, documented trade-off
# rather than an oversight.

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

  # Secrets Manager appends a random 6-character suffix to every secret's
  # ARN that isn't known until after creation, so — like the IAM role ARNs
  # above — the secretsmanager:CreateSecret grant below is scoped to a
  # wildcard pattern built from the *name* we choose, not to
  # module.secrets.secret_arn's actual (post-creation) value.
  gemini_secret_name        = "${var.project_name}/${var.environment}/gemini-api-key"
  gemini_secret_arn_pattern = "arn:aws:secretsmanager:${var.aws_region}:${local.account_id}:secret:${local.gemini_secret_name}-??????"

  # Task 5: ECR repos, ECS cluster/services/task-definitions, and the IAM
  # execution/task roles those tasks assume. Names below must exactly
  # match what modules/ecr-repo, modules/ecs-cluster, and
  # modules/fargate-service actually create (see main.tf) — like the OIDC
  # role ARNs above, these are deterministic (chosen names, not
  # AWS-assigned IDs), so they can be computed here rather than waiting on
  # module outputs. catalog_service_name/picture_service_name are defined
  # once, in main.tf's locals block, and referenced from here.
  ecs_cluster_name = "${var.project_name}-cluster"

  ecr_repository_arns = [
    "arn:aws:ecr:${var.aws_region}:${local.account_id}:repository/${var.project_name}-${local.catalog_service_name}",
    "arn:aws:ecr:${var.aws_region}:${local.account_id}:repository/${var.project_name}-${local.picture_service_name}",
  ]

  ecs_cluster_arn = "arn:aws:ecs:${var.aws_region}:${local.account_id}:cluster/${local.ecs_cluster_name}"

  ecs_service_arns = [
    "arn:aws:ecs:${var.aws_region}:${local.account_id}:service/${local.ecs_cluster_name}/${var.project_name}-${local.catalog_service_name}",
    "arn:aws:ecs:${var.aws_region}:${local.account_id}:service/${local.ecs_cluster_name}/${var.project_name}-${local.picture_service_name}",
  ]

  # RegisterTaskDefinition creates a new revision number on every call, so
  # it can only be scoped to the task-definition *family* ARN pattern
  # (trailing ":*" covering every revision), never a specific revision.
  ecs_task_definition_arns = [
    "arn:aws:ecs:${var.aws_region}:${local.account_id}:task-definition/${var.project_name}-${local.catalog_service_name}:*",
    "arn:aws:ecs:${var.aws_region}:${local.account_id}:task-definition/${var.project_name}-${local.picture_service_name}:*",
  ]

  # Only 3 roles, not 4: CatalogService has no task role at all (see
  # ecs-task-policies.tf) — just its execution role.
  fargate_iam_role_arns = [
    "arn:aws:iam::${local.account_id}:role/${var.project_name}-${local.catalog_service_name}-execution",
    "arn:aws:iam::${local.account_id}:role/${var.project_name}-${local.picture_service_name}-execution",
    "arn:aws:iam::${local.account_id}:role/${var.project_name}-${local.picture_service_name}-task",
  ]

  ecs_log_group_arns = [
    "arn:aws:logs:${var.aws_region}:${local.account_id}:log-group:/ecs/${var.project_name}-${local.catalog_service_name}:*",
    "arn:aws:logs:${var.aws_region}:${local.account_id}:log-group:/ecs/${var.project_name}-${local.picture_service_name}:*",
  ]

  # Task 9: the BFF Lambda + its execution role + its two log groups
  # (function + API Gateway access logs), and the SPA-fallback CloudFront
  # Function — all name-derived, deterministic ARNs, same reasoning as the
  # task 5 locals above (see modules/bff-lambda / modules/static-site for
  # where these exact names are chosen).
  bff_lambda_function_name = "${var.project_name}-bff"
  bff_lambda_function_arn  = "arn:aws:lambda:${var.aws_region}:${local.account_id}:function:${local.bff_lambda_function_name}"
  bff_lambda_role_arn      = "arn:aws:iam::${local.account_id}:role/${local.bff_lambda_function_name}"

  bff_lambda_log_group_arns = [
    "arn:aws:logs:${var.aws_region}:${local.account_id}:log-group:/aws/lambda/${local.bff_lambda_function_name}:*",
    "arn:aws:logs:${var.aws_region}:${local.account_id}:log-group:/aws/apigateway/${local.bff_lambda_function_name}:*",
  ]

  cloudfront_spa_fallback_function_arn = "arn:aws:cloudfront::${local.account_id}:function/${var.project_name}-spa-fallback"
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
    sid    = "Networking"
    effect = "Allow"
    actions = [
      "ec2:Describe*",
      "ec2:CreateVpc", "ec2:DeleteVpc", "ec2:ModifyVpcAttribute",
      "ec2:CreateSubnet", "ec2:DeleteSubnet", "ec2:ModifySubnetAttribute",
      "ec2:CreateInternetGateway", "ec2:DeleteInternetGateway",
      "ec2:AttachInternetGateway", "ec2:DetachInternetGateway",
      "ec2:CreateNatGateway", "ec2:DeleteNatGateway",
      "ec2:AllocateAddress", "ec2:ReleaseAddress", "ec2:AssociateAddress", "ec2:DisassociateAddress",
      "ec2:CreateRouteTable", "ec2:DeleteRouteTable", "ec2:CreateRoute", "ec2:DeleteRoute",
      "ec2:AssociateRouteTable", "ec2:DisassociateRouteTable", "ec2:ReplaceRouteTableAssociation",
      "ec2:CreateVpcEndpoint", "ec2:DeleteVpcEndpoints", "ec2:ModifyVpcEndpoint",
      "ec2:CreateTags", "ec2:DeleteTags",
      # Security groups for the Fargate tasks and the internal NLB (task
      # 5) — same "IDs are AWS-assigned, not chosen up front" scoping
      # limitation as the rest of this statement.
      "ec2:CreateSecurityGroup", "ec2:DeleteSecurityGroup",
      "ec2:AuthorizeSecurityGroupIngress", "ec2:AuthorizeSecurityGroupEgress",
      "ec2:RevokeSecurityGroupIngress", "ec2:RevokeSecurityGroupEgress",
    ]
    # See the file header comment: EC2 VPC-family resources don't support
    # resource-level IAM scoping for most of these actions.
    resources = ["*"]
  }

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

  statement {
    sid    = "GeminiSecret"
    effect = "Allow"
    actions = [
      "secretsmanager:CreateSecret", "secretsmanager:DeleteSecret",
      "secretsmanager:DescribeSecret", "secretsmanager:UpdateSecret",
      "secretsmanager:TagResource", "secretsmanager:UntagResource",
      "secretsmanager:GetResourcePolicy", "secretsmanager:PutResourcePolicy",
    ]
    resources = [local.gemini_secret_arn_pattern]
  }
}

# Split from manage_resources_core purely because a single inline IAM role
# policy is capped at 10,240 bytes by AWS — even manage_resources_core
# alone (Networking/PhotoBucket/SidecarTable/GeminiSecret) plus
# backend_access and the self-managed-IAM statements below no longer fit in
# one document once task 5's Fargate/ECS/Service-Connect/NLB permissions
# are added too.
data "aws_iam_policy_document" "manage_resources_fargate" {
  # ---- Task 5: ECR, ECS Fargate, Service Connect, internal NLB ----

  statement {
    sid       = "EcrAuth"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"] # Account/region-level action; ECR has no resource-level scoping for it.
  }

  statement {
    sid    = "ContainerRegistries"
    effect = "Allow"
    actions = [
      "ecr:CreateRepository", "ecr:DeleteRepository", "ecr:DescribeRepositories",
      "ecr:PutLifecyclePolicy", "ecr:GetLifecyclePolicy", "ecr:DeleteLifecyclePolicy",
      "ecr:PutImageScanningConfiguration", "ecr:TagResource", "ecr:UntagResource", "ecr:ListTagsForResource",
      # Actual image push/pull — also what the future CI deploy workflows
      # (task 10.2/10.3) need, since they assume this same role.
      "ecr:BatchCheckLayerAvailability", "ecr:GetDownloadUrlForLayer", "ecr:BatchGetImage",
      "ecr:InitiateLayerUpload", "ecr:UploadLayerPart", "ecr:CompleteLayerUpload", "ecr:PutImage",
    ]
    resources = local.ecr_repository_arns
  }

  statement {
    sid    = "EcsCluster"
    effect = "Allow"
    actions = [
      "ecs:CreateCluster", "ecs:DeleteCluster", "ecs:DescribeClusters", "ecs:UpdateCluster",
      "ecs:PutClusterCapacityProviders", "ecs:TagResource", "ecs:UntagResource",
    ]
    resources = [local.ecs_cluster_arn]
  }

  statement {
    sid    = "EcsServices"
    effect = "Allow"
    actions = [
      "ecs:CreateService", "ecs:UpdateService", "ecs:DeleteService", "ecs:DescribeServices",
      "ecs:TagResource", "ecs:UntagResource",
    ]
    resources = local.ecs_service_arns
  }

  statement {
    sid    = "EcsTaskDefinitions"
    effect = "Allow"
    actions = [
      "ecs:RegisterTaskDefinition", "ecs:DeregisterTaskDefinition",
      "ecs:TagResource", "ecs:UntagResource",
    ]
    resources = local.ecs_task_definition_arns
  }

  statement {
  +  sid       = "EcsDescribeTaskDefinitions"
  +  effect    = "Allow"
  +  actions   = ["ecs:DescribeTaskDefinition"]
  +  resources = ["*"]
+ }

  statement {
    sid       = "EcsListAndDescribeTasks"
    effect    = "Allow"
    actions   = ["ecs:ListTaskDefinitions", "ecs:DescribeTasks", "ecs:ListTasks"]
    resources = ["*"] # List actions don't support resource-level scoping.
  }

  statement {
    sid    = "FargateTaskIamRoles"
    effect = "Allow"
    actions = [
      "iam:CreateRole", "iam:DeleteRole", "iam:GetRole",
      "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy", "iam:ListRolePolicies",
      "iam:TagRole", "iam:UntagRole",
    ]
    resources = local.fargate_iam_role_arns
  }

  statement {
    sid       = "PassFargateTaskRoles"
    effect    = "Allow"
    actions   = ["iam:PassRole"]
    resources = local.fargate_iam_role_arns

    condition {
      test     = "StringEquals"
      variable = "iam:PassedToService"
      values   = ["ecs-tasks.amazonaws.com"]
    }
  }

  statement {
    sid    = "EcsLogGroups"
    effect = "Allow"
    actions = [
      "logs:CreateLogGroup", "logs:DeleteLogGroup", "logs:DescribeLogGroups",
      "logs:PutRetentionPolicy", "logs:TagResource", "logs:UntagResource",
    ]
    resources = local.ecs_log_group_arns
  }

  statement {
    sid    = "ServiceConnectNamespace"
    effect = "Allow"
    actions = [
      "servicediscovery:CreateHttpNamespace", "servicediscovery:DeleteNamespace",
      "servicediscovery:GetNamespace", "servicediscovery:ListNamespaces",
      "servicediscovery:TagResource", "servicediscovery:UntagResource", "servicediscovery:GetOperation",
    ]
    # Cloud Map namespace IDs (ns-xxxxxxxxx) are AWS-assigned at creation —
    # same "can't scope an ARN that doesn't exist yet" situation as the
    # EC2 networking statement above — action allow-list only.
    resources = ["*"]
  }

  statement {
    sid    = "InternalLoadBalancer"
    effect = "Allow"
    actions = [
      "elasticloadbalancing:CreateLoadBalancer", "elasticloadbalancing:DeleteLoadBalancer",
      "elasticloadbalancing:DescribeLoadBalancers", "elasticloadbalancing:ModifyLoadBalancerAttributes",
      "elasticloadbalancing:DescribeLoadBalancerAttributes",
      "elasticloadbalancing:CreateTargetGroup", "elasticloadbalancing:DeleteTargetGroup",
      "elasticloadbalancing:DescribeTargetGroups", "elasticloadbalancing:ModifyTargetGroup",
      "elasticloadbalancing:ModifyTargetGroupAttributes", "elasticloadbalancing:DescribeTargetGroupAttributes",
      "elasticloadbalancing:CreateListener", "elasticloadbalancing:DeleteListener",
      "elasticloadbalancing:DescribeListeners", "elasticloadbalancing:ModifyListener",
      "elasticloadbalancing:AddTags", "elasticloadbalancing:RemoveTags", "elasticloadbalancing:DescribeTags",
    ]
    # NLB/target-group/listener ARNs also carry an AWS-assigned ID
    # component, the same scoping limitation as the Cloud Map namespace
    # above.
    resources = ["*"]
  }

  statement {
    sid    = "SelfManagedIamRoles"
    effect = "Allow"
    actions = [
      "iam:GetRole", "iam:CreateRole", "iam:DeleteRole", "iam:UpdateRole",
      "iam:GetRolePolicy", "iam:PutRolePolicy", "iam:DeleteRolePolicy",
      "iam:TagRole", "iam:UntagRole", "iam:ListRolePolicies",
      "iam:UpdateAssumeRolePolicy",
    ]
    resources = local.self_iam_role_arns
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

# Split from manage_resources_core purely because a single inline IAM role
# policy is capped at 10,240 bytes by AWS — this stack's combined deploy
# permissions no longer fit in one document. As with task 5's statements
# above, several of these double as what the future CI deploy workflows
# need at runtime: task 10.4's `lambda update-function-code`
# (BffLambdaFunction statement's UpdateFunctionCode action) and task 10.5's
# `s3 sync` + `cloudfront create-invalidation` (StaticSiteBucket's
# PutObject/DeleteObject and CloudFrontDistribution's CreateInvalidation
# actions).
data "aws_iam_policy_document" "manage_resources_web_hosting" {
  statement {
    sid    = "BffLambdaFunction"
    effect = "Allow"
    actions = [
      "lambda:CreateFunction", "lambda:DeleteFunction", "lambda:GetFunction", "lambda:GetFunctionConfiguration",
      "lambda:UpdateFunctionCode", "lambda:UpdateFunctionConfiguration", "lambda:TagResource", "lambda:UntagResource",
      "lambda:ListVersionsByFunction", "lambda:GetPolicy", "lambda:AddPermission", "lambda:RemovePermission",
      "lambda:PutProvisionedConcurrencyConfig", "lambda:DeleteProvisionedConcurrencyConfig",
      "lambda:GetProvisionedConcurrencyConfig",
    ]
    resources = [local.bff_lambda_function_arn]
  }

  statement {
    sid    = "BffLambdaIamRole"
    effect = "Allow"
    actions = [
      "iam:CreateRole", "iam:DeleteRole", "iam:GetRole",
      "iam:PutRolePolicy", "iam:DeleteRolePolicy", "iam:GetRolePolicy", "iam:ListRolePolicies",
      "iam:TagRole", "iam:UntagRole",
    ]
    resources = [local.bff_lambda_role_arn]
  }

  statement {
    sid    = "PassBffLambdaRole"
    effect = "Allow"
    actions = ["iam:PassRole"]
    resources = [local.bff_lambda_role_arn]

    condition {
      test     = "StringEquals"
      variable = "iam:PassedToService"
      values   = ["lambda.amazonaws.com"]
    }
  }

  statement {
    sid    = "BffLambdaLogGroups"
    effect = "Allow"
    actions = [
      "logs:CreateLogGroup", "logs:DeleteLogGroup", "logs:DescribeLogGroups",
      "logs:PutRetentionPolicy", "logs:TagResource", "logs:UntagResource",
    ]
    resources = local.bff_lambda_log_group_arns
  }

  statement {
    sid    = "ApiGatewayManagement"
    effect = "Allow"
    actions = [
      "apigateway:GET", "apigateway:POST", "apigateway:PUT", "apigateway:PATCH", "apigateway:DELETE",
      "apigateway:TagResource", "apigateway:UntagResource",
    ]
    # API Gateway IAM uses this REST-style resource-path ARN convention
    # (not a per-API-ID ARN) regardless of API type — the trailing "/*"
    # covers the HTTP API and its routes/integrations/stages once created,
    # whose own IDs are AWS-assigned (same "can't scope an ARN that doesn't
    # exist yet" situation as the Cloud Map namespace / internal NLB
    # statements above).
    resources = ["arn:aws:apigateway:${var.aws_region}::/apis", "arn:aws:apigateway:${var.aws_region}::/apis/*"]
  }

  statement {
    sid    = "CloudFrontDistribution"
    effect = "Allow"
    actions = [
      "cloudfront:CreateDistribution", "cloudfront:GetDistribution", "cloudfront:UpdateDistribution",
      "cloudfront:DeleteDistribution", "cloudfront:ListDistributions", "cloudfront:TagResource", "cloudfront:UntagResource",
      "cloudfront:CreateOriginAccessControl", "cloudfront:GetOriginAccessControl",
      "cloudfront:UpdateOriginAccessControl", "cloudfront:DeleteOriginAccessControl",
      # Runtime: task 10.5's post-sync cache invalidation.
      "cloudfront:CreateInvalidation", "cloudfront:GetInvalidation",
    ]
    # Distribution/OAC IDs are AWS-assigned at creation — same scoping
    # limitation as the internal NLB statement above.
    resources = ["*"]
  }

  statement {
    sid    = "CloudFrontSpaFallbackFunction"
    effect = "Allow"
    actions = [
      "cloudfront:CreateFunction", "cloudfront:UpdateFunction", "cloudfront:DeleteFunction",
      "cloudfront:DescribeFunction", "cloudfront:GetFunction", "cloudfront:PublishFunction",
      "cloudfront:TagResource", "cloudfront:UntagResource",
    ]
    resources = [local.cloudfront_spa_fallback_function_arn]
  }

  statement {
    sid    = "StaticSiteBucket"
    effect = "Allow"
    actions = [
      "s3:CreateBucket", "s3:DeleteBucket",
      "s3:GetBucket*", "s3:PutBucket*",
      "s3:GetEncryptionConfiguration", "s3:PutEncryptionConfiguration",
      "s3:GetBucketPolicy", "s3:PutBucketPolicy", "s3:DeleteBucketPolicy",
      "s3:PutBucketPublicAccessBlock", "s3:GetBucketPublicAccessBlock",
      "s3:PutBucketTagging", "s3:GetBucketTagging",
      # Runtime: task 10.5's `aws s3 sync` publishing the WASM client's
      # build output.
      "s3:PutObject", "s3:GetObject", "s3:DeleteObject", "s3:ListBucket",
    ]
    resources = [module.web_client.bucket_arn, "${module.web_client.bucket_arn}/*"]
  }
}

data "aws_iam_policy_document" "deploy_core" {
  source_policy_documents = [
    data.aws_iam_policy_document.backend_access.json,
    data.aws_iam_policy_document.manage_resources_core.json,
  ]
}

# ---- Read-only variant for the plan role ----
# `terraform plan` needs to *read* every resource type apply can write, but
# never needs the write verbs — same resource scoping as manage_resources,
# with each statement's actions swapped for their Describe/Get/List
# equivalents.
data "aws_iam_policy_document" "plan_only" {
  statement {
    sid       = "NetworkingRead"
    effect    = "Allow"
    actions   = ["ec2:Describe*"]
    resources = ["*"]
  }

  statement {
    sid    = "StorageAndSecretRead"
    effect = "Allow"
    actions = [
      "s3:GetBucket*", "s3:GetLifecycleConfiguration", "s3:GetEncryptionConfiguration",
      "s3:GetBucketPolicy", "s3:GetBucketVersioning", "s3:GetBucketCORS",
      "s3:GetBucketPublicAccessBlock", "s3:GetBucketTagging",
      "dynamodb:DescribeTable", "dynamodb:ListTagsOfResource",
      "dynamodb:DescribeContinuousBackups", "dynamodb:DescribeTimeToLive",
      "secretsmanager:DescribeSecret", "secretsmanager:GetResourcePolicy",
    ]
    resources = [
      module.photo_storage.bucket_arn,
      module.sidecar_table.table_arn,
      "${module.sidecar_table.table_arn}/index/*",
      local.gemini_secret_arn_pattern,
    ]
  }

  statement {
    sid       = "SelfManagedIamRolesRead"
    effect    = "Allow"
    actions   = ["iam:GetRole", "iam:GetRolePolicy", "iam:ListRolePolicies"]
    resources = local.self_iam_role_arns
  }

  statement {
    sid    = "ContainerComputeRead"
    effect = "Allow"
    actions = [
      "ecr:DescribeRepositories", "ecr:GetLifecyclePolicy", "ecr:ListTagsForResource",
      "ecs:DescribeClusters", "ecs:DescribeServices", "ecs:DescribeTaskDefinition",
      "ecs:ListTaskDefinitions", "ecs:DescribeTasks", "ecs:ListTasks",
      "servicediscovery:GetNamespace", "servicediscovery:ListNamespaces",
      "elasticloadbalancing:DescribeLoadBalancers", "elasticloadbalancing:DescribeLoadBalancerAttributes",
      "elasticloadbalancing:DescribeTargetGroups", "elasticloadbalancing:DescribeTargetGroupAttributes",
      "elasticloadbalancing:DescribeListeners", "elasticloadbalancing:DescribeTags",
      "logs:DescribeLogGroups",
    ]
    resources = ["*"]
  }

  statement {
    sid       = "FargateTaskIamRolesRead"
    effect    = "Allow"
    actions   = ["iam:GetRole", "iam:GetRolePolicy", "iam:ListRolePolicies"]
    resources = local.fargate_iam_role_arns
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

  # ---- Task 9 read-only mirror ----

  statement {
    sid    = "BffAndCdnRead"
    effect = "Allow"
    actions = [
      "lambda:GetFunction", "lambda:GetFunctionConfiguration", "lambda:ListVersionsByFunction",
      "lambda:GetPolicy", "lambda:GetProvisionedConcurrencyConfig",
      "apigateway:GET",
      "cloudfront:GetDistribution", "cloudfront:ListDistributions", "cloudfront:GetOriginAccessControl",
      "cloudfront:GetFunction", "cloudfront:DescribeFunction",
      "logs:DescribeLogGroups",
    ]
    resources = ["*"]
  }

  statement {
    sid       = "BffLambdaRoleRead"
    effect    = "Allow"
    actions   = ["iam:GetRole", "iam:GetRolePolicy", "iam:ListRolePolicies"]
    resources = [local.bff_lambda_role_arn]
  }

  statement {
    sid    = "StaticSiteBucketRead"
    effect = "Allow"
    actions = [
      "s3:GetBucket*", "s3:GetBucketPolicy", "s3:GetBucketPublicAccessBlock",
      "s3:GetBucketTagging", "s3:GetEncryptionConfiguration",
    ]
    resources = [module.web_client.bucket_arn]
  }
}

data "aws_iam_policy_document" "plan" {
  source_policy_documents = [
    data.aws_iam_policy_document.backend_access.json,
    data.aws_iam_policy_document.plan_only.json,
  ]
}
