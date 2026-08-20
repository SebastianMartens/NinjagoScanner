# The Web BFF (NinjagoScanner.Web.Bff) on Lambda + API Gateway (HTTP API) —
# task 9.1. VPC-attached so it can reach CatalogService/PictureService on
# Fargate via the internal NLB (modules/internal-lb; see that module's header
# comment for why this is an NLB hop and not ECS Service Connect — a Lambda
# function can never join the Service Connect mesh no matter how it's wired).
#
# ---- One IAM role, not two ----
# modules/fargate-service deliberately splits an *execution* role (assumed by
# the ECS agent — image pull, logs, secrets resolution) from a *task* role
# (assumed by the application code via the AWS SDK), because ECS actually
# hands two different principals two different sets of credentials inside
# one running task. Lambda has no equivalent split: there is exactly one
# execution role, assumed by the Lambda service on the function's behalf,
# and it's also the identity the AWS SDK inside the function code runs as
# (credentials come from the same execution environment either way). So this
# module creates one role covering both concerns:
#   - what the Lambda *platform* needs regardless of what the code does:
#     CloudWatch Logs, and ENI create/describe/delete for VPC attachment
#     (the hand-written equivalent of the AWS-managed
#     AWSLambdaVPCAccessExecutionRole policy — written out explicitly here
#     rather than attached as a managed policy, matching this repo's
#     least-privilege convention elsewhere).
#   - what NinjagoScanner.Web.Bff's own code calls: S3UploadUrlIssuer's two
#     presigned-URL calls (GetPreSignedURLAsync for PUT and GET). Presigning
#     itself is a local SigV4 computation — no network call, so no IAM check
#     happens at signing time — but the *use* of that URL (the browser's
#     actual PUT/GET against S3) is authorized against this role's own
#     permissions at request time, because a presigned URL only carries as
#     much authority as its signer has. So this role needs real
#     s3:PutObject (for upload URLs) and s3:GetObject (for download URLs) on
#     the photos/* prefix — unlike PictureService's own ECS task role
#     (environments/prod/ecs-task-policies.tf), which deliberately has no
#     PutObject because PictureService's own code never writes photo bytes
#     itself. The BFF is the one place PutObject belongs, precisely because
#     it's the thing minting upload authority for the browser.
#
# ---- Runtime: provided.al2023 (self-contained), not a managed dotnet
# runtime ----
# NinjagoScanner.Web.Bff targets net10.0. As of this writing AWS Lambda's
# managed .NET runtimes lag new .NET releases by some months, and this
# environment has no AWS CLI/credentials to check what's actually available
# in eu-central-1 today. Rather than gamble on a "dotnet10" managed runtime
# existing, this module targets the custom runtime family
# (provided.al2023) with a self-contained deployment package — the
# AWS-documented pattern for running ASP.NET Core Minimal APIs (exactly what
# Amazon.Lambda.AspNetCoreServer.Hosting's AddAWSLambdaHosting wires up in
# Program.cs) on Lambda regardless of managed-runtime availability. The
# deploy workflow (task 10.4, not built yet) is expected to run
# `dotnet publish -r linux-arm64 --self-contained true` and package the
# resulting output as `bootstrap` at the zip root. If AWS ships a dotnet10
# managed runtime before task 10.4 is written and a smaller deployment
# package is preferred, switching `runtime` below to it is a one-line change
# — nothing else here depends on which runtime family is used.
#
# ---- Architecture: arm64 ----
# Graviton (arm64) Lambda pricing is lower than x86_64 for the same
# memory/duration, and .NET's self-contained publish supports linux-arm64
# natively — matches this stack's existing cost-consciousness (single NAT
# Gateway, Container Insights off, etc.).
#
# ---- Deployment package: a placeholder Terraform manages once, CI owns
# after that ----
# aws_lambda_function requires a real zip to exist at creation time — unlike
# ECS, which can happily reference an image tag that doesn't exist yet in
# ECR and just fail to start tasks (see catalog_service_image_tag's
# variable description). So this module generates a trivial placeholder zip
# via the archive_file data source and marks `filename`/`source_code_hash`
# as lifecycle-ignored: the *first* apply creates the function with this
# placeholder (which would fail any real invocation — expected and fine,
# mirroring the Fargate services having no real image on first apply
# either), and every apply after that leaves the function's actual code
# alone, because task 10.4's deploy workflow owns updating it via
# `aws lambda update-function-code`, not Terraform.

locals {
  function_name = "${var.project_name}-${var.function_name}"
}

data "archive_file" "placeholder" {
  type        = "zip"
  output_path = "${path.module}/.placeholder.zip"

  source {
    content  = "Replaced by the BFF deploy workflow (task 10.4, aws lambda update-function-code) — this placeholder only exists so `aws_lambda_function` has a real zip to create against on the first `terraform apply`."
    filename = "PLACEHOLDER"
  }
}

# ---- Execution role ----

data "aws_iam_policy_document" "assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "lambda" {
  name               = local.function_name
  assume_role_policy = data.aws_iam_policy_document.assume_role.json

  tags = var.tags
}

resource "aws_cloudwatch_log_group" "lambda" {
  name              = "/aws/lambda/${local.function_name}"
  retention_in_days = var.log_retention_days

  tags = var.tags
}

data "aws_iam_policy_document" "lambda" {
  statement {
    sid    = "Logs"
    effect = "Allow"
    actions = [
      "logs:CreateLogStream",
      "logs:PutLogEvents",
    ]
    resources = ["${aws_cloudwatch_log_group.lambda.arn}:*"]
  }

  # AWSLambdaVPCAccessExecutionRole's equivalent, written out explicitly
  # rather than attached as an AWS-managed policy. ENI IDs are AWS-assigned
  # at creation, so — like the EC2 networking statement in
  # environments/prod/iam-policies.tf — this can only be scoped by action
  # allow-list, not by resource ARN.
  statement {
    sid    = "VpcEniManagement"
    effect = "Allow"
    actions = [
      "ec2:CreateNetworkInterface",
      "ec2:DescribeNetworkInterfaces",
      "ec2:DeleteNetworkInterface",
      "ec2:AssignPrivateIpAddresses",
      "ec2:UnassignPrivateIpAddresses",
    ]
    resources = ["*"]
  }

  # S3UploadUrlIssuer.cs — see this file's header comment for why PutObject
  # belongs here (and not on PictureService's task role).
  statement {
    sid    = "PresignedPhotoUrls"
    effect = "Allow"
    actions = [
      "s3:PutObject",
      "s3:GetObject",
    ]
    resources = ["${var.photos_bucket_arn}/photos/*"]
  }
}

resource "aws_iam_role_policy" "lambda" {
  name   = "${local.function_name}-policy"
  role   = aws_iam_role.lambda.id
  policy = data.aws_iam_policy_document.lambda.json
}

# ---- Networking ----

resource "aws_security_group" "lambda" {
  name        = "${local.function_name}-sg"
  description = "BFF Lambda (task 9.1) - VPC-attached to reach CatalogService/PictureService via the internal NLB. No ingress: nothing calls this functions ENIs directly, only API Gateway invokes the function itself (outside the VPC data path)."
  vpc_id      = var.vpc_id

  egress {
    description      = "CatalogService, via the internal NLB (modules/internal-lb)"
    from_port        = var.catalog_service_listener_port
    to_port          = var.catalog_service_listener_port
    protocol         = "tcp"
    security_groups  = [var.internal_lb_security_group_id]
  }

  egress {
    description      = "PictureService, via the internal NLB (modules/internal-lb)"
    from_port        = var.picture_service_listener_port
    to_port          = var.picture_service_listener_port
    protocol         = "tcp"
    security_groups  = [var.internal_lb_security_group_id]
  }

  # S3UploadUrlIssuer.cs's GetPreSignedURLAsync calls are local signing
  # operations with no network call, so this isn't strictly required for
  # today's code — kept as a documented, minimal allowance (scoped to
  # AWS's S3 prefix list, not 0.0.0.0/0) in case the AWS SDK's credential
  # resolution or any future direct S3 call needs it. Traffic on this path
  # never leaves the VPC: the networking module's S3 gateway VPC endpoint
  # (modules/networking) covers it without a NAT Gateway hop.
  egress {
    description     = "S3 (photos bucket), via the networking modules S3 gateway VPC endpoint"
    from_port       = 443
    to_port         = 443
    protocol        = "tcp"
    prefix_list_ids = [data.aws_prefix_list.s3.id]
  }

  tags = merge(var.tags, { Name = "${local.function_name}-sg" })
}

data "aws_prefix_list" "s3" {
  name = "com.amazonaws.${var.aws_region}.s3"
}

# ---- Lambda function ----

resource "aws_lambda_function" "bff" {
  function_name = local.function_name
  role          = aws_iam_role.lambda.arn

  # See this file's header comment on the provided.al2023/self-contained
  # choice. "bootstrap" is the AWS-documented handler value for custom
  # runtimes — the runtime always executes ./bootstrap regardless of what's
  # configured here, this field is effectively unused for provided.al2023
  # but still required by the resource schema.
  handler       = "bootstrap"
  runtime       = "provided.al2023"
  architectures = ["arm64"]

  filename         = data.archive_file.placeholder.output_path
  source_code_hash = data.archive_file.placeholder.output_base64sha256

  memory_size = var.memory_size
  timeout     = var.timeout_seconds

  vpc_config {
    subnet_ids         = var.private_subnet_ids
    security_group_ids = [aws_security_group.lambda.id]
  }

  environment {
    variables = {
      # BffConfig.ResolveCatalogServiceAddress / ResolvePictureServiceAddress
      # — reachable via the internal NLB (modules/internal-lb), not Service
      # Connect (see that module's header comment for why).
      "CatalogService__Address" = "http://${var.internal_lb_dns_name}:${var.catalog_service_listener_port}"
      "PictureService__Address" = "http://${var.internal_lb_dns_name}:${var.picture_service_listener_port}"
      # BffConfig.ResolvePhotosBucketName.
      "Storage__PhotosBucketName" = var.photos_bucket_name
      # No Cors:ClientOrigin is set here on purpose: environments/prod's
      # CloudFront distribution (modules/static-site) serves the WASM
      # client and proxies /api/* to this function's API Gateway endpoint
      # from the *same* origin (task 9.3), so the browser's requests to
      # /api/* are same-origin and never trigger a CORS check at all —
      # Program.cs's AllowAnyOrigin() fallback stays permissive but is
      # functionally moot in production. See infra/README.md's task 9
      # section for the full reasoning, including why this can't simply be
      # wired to the CloudFront domain instead (a real dependency cycle:
      # this function's own bucket/NLB inputs are upstream of the
      # CloudFront distribution that would supply that value).
    }
  }

  # Lifecycle: Terraform creates the function once against the placeholder
  # package above; every deploy after that is `aws lambda
  # update-function-code`, run by task 10.4's CI workflow using this same
  # `deploy` IAM role — not another `terraform apply`. Without this,
  # `terraform apply` would silently revert whatever CI last deployed back
  # to the placeholder.
  lifecycle {
    ignore_changes = [filename, source_code_hash]
  }

  tags = var.tags
}

resource "aws_lambda_provisioned_concurrency_config" "bff" {
  count = var.provisioned_concurrency > 0 ? 1 : 0

  function_name                     = aws_lambda_function.bff.function_name
  qualifier                         = aws_lambda_function.bff.version
  provisioned_concurrent_executions = var.provisioned_concurrency
}

# ---- API Gateway (HTTP API) ----
# HTTP API, not REST API: cheaper, and matches LambdaEventSource.HttpApi
# already wired up in Program.cs (AddAWSLambdaHosting(LambdaEventSource.HttpApi)).

resource "aws_apigatewayv2_api" "bff" {
  name          = "${local.function_name}-api"
  protocol_type = "HTTP"

  tags = var.tags
}

resource "aws_apigatewayv2_integration" "lambda" {
  api_id                 = aws_apigatewayv2_api.bff.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.bff.invoke_arn
  payload_format_version = "2.0"
}

# Single catch-all route: Program.cs owns all real routing (the "/api"
# MapGroup and its endpoints) via ASP.NET Core's own request pipeline once
# the proxied request reaches the function, so API Gateway itself doesn't
# need per-endpoint route definitions — modules/static-site's CloudFront
# distribution is what actually restricts what reaches this API in the
# first place (only path pattern "/api/*", task 9.3).
resource "aws_apigatewayv2_route" "default" {
  api_id    = aws_apigatewayv2_api.bff.id
  route_key = "$default"
  target    = "integrations/${aws_apigatewayv2_integration.lambda.id}"
}

resource "aws_cloudwatch_log_group" "api_gateway" {
  name              = "/aws/apigateway/${local.function_name}"
  retention_in_days = var.log_retention_days

  tags = var.tags
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.bff.id
  name        = "$default"
  auto_deploy = true

  access_log_settings {
    destination_arn = aws_cloudwatch_log_group.api_gateway.arn
    format = jsonencode({
      requestId      = "$context.requestId"
      routeKey       = "$context.routeKey"
      status         = "$context.status"
      integrationErr = "$context.integrationErrorMessage"
      responseTime   = "$context.responseLatency"
    })
  }

  tags = var.tags
}

resource "aws_lambda_permission" "api_gateway" {
  statement_id  = "AllowApiGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.bff.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.bff.execution_arn}/*/*"
}
