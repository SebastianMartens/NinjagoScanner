# Generic ECS Fargate service: task definition + service, IAM
# execution/task roles, CloudWatch log group, and Service Connect
# registration. Instantiated once each for CatalogService and PictureService
# (task 5.2/5.3 — see environments/prod/main.tf), parameterized by image,
# port, resource sizing, and each service's own AWS permissions.
#
# ---- Two IAM roles, not one ----
# A deliberate split with different purposes, both required by ECS:
#   - *execution* role: assumed by the ECS agent itself (never by
#     application code) to pull the container image from ECR, write
#     CloudWatch Logs, and resolve `secrets` entries from Secrets
#     Manager into container environment variables at task startup. Every
#     service gets one of these, unconditionally.
#   - *task* role: assumed by the application code inside the running
#     container, via the AWS SDK, for whatever AWS APIs it calls directly
#     at runtime (S3, DynamoDB, ...). Only created when
#     `task_role_policy_json` is non-null. CatalogService passes null — it
#     only reads local cardInfos/*.json baked into its own image (see
#     CatalogRepository.cs), no AWS API calls anywhere in its code — so it
#     gets no task role at all, rather than an empty-but-present one; that
#     is what "no AWS resource access beyond default ECS/CloudWatch Logs
#     permissions" (task 5.2) means literally. PictureService passes a
#     policy scoped to exactly what PhotoStore.cs/SidecarTable.cs call —
#     see environments/prod/ecs-task-policies.tf for that policy and the
#     code-level justification for each statement.
#
# Note on Secrets Manager specifically: PictureService's Gemini API key is
# wired in via the `secrets` list below, resolved by the *execution* role,
# not the task role — GeminiApiService.cs/ScannerConfig.cs only ever read
# it back out of IConfiguration (i.e. from the environment variable ECS
# already populated), they never call the Secrets Manager SDK directly. So
# secretsmanager:GetSecretValue belongs on the execution role
# (secrets_manager_arns below), not on the task role.

data "aws_region" "current" {}

data "aws_iam_policy_document" "execution_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "execution" {
  name               = "${var.project_name}-${var.service_name}-execution"
  assume_role_policy = data.aws_iam_policy_document.execution_assume_role.json

  tags = var.tags
}

resource "aws_cloudwatch_log_group" "this" {
  name              = "/ecs/${var.project_name}-${var.service_name}"
  retention_in_days = var.log_retention_days

  tags = var.tags
}

data "aws_iam_policy_document" "execution_policy" {
  statement {
    sid       = "EcrAuth"
    effect    = "Allow"
    actions   = ["ecr:GetAuthorizationToken"]
    resources = ["*"] # Account/region-level action; ECR has no resource-level scoping for it.
  }

  statement {
    sid    = "EcrPull"
    effect = "Allow"
    actions = [
      "ecr:BatchGetImage",
      "ecr:GetDownloadUrlForLayer",
    ]
    resources = [var.ecr_repository_arn]
  }

  statement {
    sid    = "Logs"
    effect = "Allow"
    actions = [
      "logs:CreateLogStream",
      "logs:PutLogEvents",
    ]
    resources = ["${aws_cloudwatch_log_group.this.arn}:*"]
  }

  dynamic "statement" {
    for_each = length(var.secrets_manager_arns) > 0 ? [1] : []
    content {
      sid       = "ResolveSecrets"
      effect    = "Allow"
      actions   = ["secretsmanager:GetSecretValue"]
      resources = var.secrets_manager_arns
    }
  }
}

resource "aws_iam_role_policy" "execution" {
  name   = "${var.project_name}-${var.service_name}-execution-policy"
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.execution_policy.json
}

# ---- Task role (optional — application-level AWS access) ----

data "aws_iam_policy_document" "task_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "task" {
  count = var.create_task_role ? 1 : 0

  name               = "${var.project_name}-${var.service_name}-task"
  assume_role_policy = data.aws_iam_policy_document.task_assume_role.json

  tags = var.tags
}

resource "aws_iam_role_policy" "task" {
  count = var.create_task_role ? 1 : 0

  name   = "${var.project_name}-${var.service_name}-task-policy"
  role   = aws_iam_role.task[0].id
  policy = var.task_role_policy_json
}

# ---- Networking ----

resource "aws_security_group" "task" {
  name        = "${var.project_name}-${var.service_name}-sg"
  description = "Fargate task security group for ${var.service_name}. Ingress from inside the VPC only (Service Connect traffic from other tasks, and the internal NLB health checks/traffic - see modules/internal-lb) - no path from the public internet, per task 5.4."
  vpc_id      = var.vpc_id

  ingress {
    description = "Container port, from inside the VPC (Service Connect peers + internal NLB)"
    from_port   = var.container_port
    to_port     = var.container_port
    protocol    = "tcp"
    cidr_blocks = [var.vpc_cidr]
  }

  egress {
    # Needed for: pulling the image from ECR, writing CloudWatch Logs,
    # PictureService's outbound HTTPS calls to the Gemini API
    # (generativelanguage.googleapis.com), and reaching the S3/DynamoDB
    # gateway VPC endpoints — all routed out through the private subnet's
    # NAT Gateway or a gateway VPC endpoint, never inbound from outside.
    description = "All outbound"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, { Name = "${var.project_name}-${var.service_name}-sg" })
}

# ---- Task definition + service ----

resource "aws_ecs_task_definition" "this" {
  family                   = "${var.project_name}-${var.service_name}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.cpu
  memory                   = var.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn             = var.create_task_role ? aws_iam_role.task[0].arn : null

  container_definitions = jsonencode([
    {
      name      = var.service_name
      image     = var.container_image
      essential = true

      portMappings = [
        {
          name          = var.service_connect_discovery_name
          containerPort = var.container_port
          protocol      = "tcp"
        }
      ]

      environment = [for key, value in var.environment_variables : { name = key, value = value }]
      secrets     = [for secret in var.secrets : { name = secret.name, valueFrom = secret.value_from }]

      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = aws_cloudwatch_log_group.this.name
          "awslogs-region"        = data.aws_region.current.name
          "awslogs-stream-prefix" = var.service_name
        }
      }
    }
  ])

  tags = var.tags
}

resource "aws_ecs_service" "this" {
  name            = "${var.project_name}-${var.service_name}"
  cluster         = var.cluster_id
  task_definition = aws_ecs_task_definition.this.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.task.id]
    assign_public_ip = false
  }

  # Server + client Service Connect registration (task 5.3). Every service
  # gets a `service` block — including PictureService, even though nothing
  # calls it over Service Connect today (only CatalogService is called that
  # way, by PictureService; PictureService itself is reached by the BFF via
  # modules/internal-lb) — registering it costs nothing and keeps this
  # module uniform rather than special-casing "server" vs. "client-only"
  # services.
  service_connect_configuration {
    enabled   = true
    namespace = var.service_connect_namespace_arn

    service {
      port_name      = var.service_connect_discovery_name
      discovery_name = var.service_connect_discovery_name

      client_alias {
        port     = var.container_port
        dns_name = var.service_connect_discovery_name
      }
    }

    log_configuration {
      log_driver = "awslogs"
      options = {
        "awslogs-group"         = aws_cloudwatch_log_group.this.name
        "awslogs-region"        = data.aws_region.current.name
        "awslogs-stream-prefix" = "${var.service_name}-service-connect"
      }
    }
  }

  # Registers with the shared internal NLB (modules/internal-lb) so the BFF
  # Lambda can reach this service — see that module's header comment for why
  # Service Connect alone can't cover this path.
  dynamic "load_balancer" {
    for_each = var.load_balancer_target_group_arn == null ? [] : [1]
    content {
      target_group_arn = var.load_balancer_target_group_arn
      container_name    = var.service_name
      container_port    = var.container_port
    }
  }

  tags = var.tags
}
