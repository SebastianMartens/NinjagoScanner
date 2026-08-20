# Internal (private) Network Load Balancer — the reachability path from the
# Web BFF Lambda (task 9, VPC-attached, not built yet) to CatalogService and
# PictureService on Fargate.
#
# ---- Why this exists: task 5.4's open question, resolved ----
#
# Task 5.4 asks us to confirm neither CatalogService nor PictureService
# needs *public* reachability, since only the BFF and each other call them.
# That much is easy to confirm — proposal.md and design.md are explicit that
# nothing outside the VPC calls either service directly, and no page/client
# talks to them over the public internet. So: no public ALB is provisioned
# anywhere in this stack. Both services are reachable only from inside the
# VPC, full stop.
#
# The harder part of 5.4 is *how* the BFF reaches them, because design.md's
# Networking decision ("the BFF Lambda ... reaches them via ECS Service
# Connect's internal DNS namespace") turns out not to hold up technically:
# Service Connect's internal names are resolved by the Envoy-based proxy
# sidecar ECS injects into each participating *ECS task*'s network
# namespace (see modules/ecs-cluster's comment) — there is no mechanism for
# a Lambda function, even one attached to the same VPC/subnets, to receive
# that sidecar or otherwise join the Service Connect mesh. A VPC-attached
# Lambda resolves names via the VPC's normal Route 53 Resolver, which has no
# visibility into Service Connect's task-local proxy state at all. In short:
# Service Connect works great for PictureService -> CatalogService (both are
# ECS tasks, wired up in modules/fargate-service), but it cannot be the
# BFF's path to either service, no matter how it's configured.
#
# So this module provides the missing piece: a private, internal-only load
# balancer with a real DNS name resolvable by anything in the VPC — Lambda
# included — fronting both services' Fargate tasks. `internal = true` means
# no public IP and no route from the internet; this is not a variant of the
# public ALB design.md originally sketched, it replaces it for this
# specific hop.
#
# NLB (not ALB) is used deliberately: CatalogService and PictureService
# speak plaintext HTTP/2 gRPC (no TLS) today, matching the existing
# same-machine setup (see design.md's Context section) — an ALB only
# supports HTTP/2 on an HTTPS listener, which would mean provisioning and
# rotating an ACM cert for a hop that never leaves the VPC. An NLB is a pure
# L4 TCP passthrough, so it carries HTTP/2 cleartext transparently with no
# cert needed, at the cost of losing ALB's HTTP-aware routing (irrelevant
# here — the BFF talks to each service on its own dedicated listener port,
# no path-based routing is needed).
#
# One NLB shared by both services (two listeners, two target groups) rather
# than one each, matching this stack's existing cost-consciousness (see
# networking module's single_nat_gateway default) — a second NLB roughly
# doubles the fixed hourly cost for no capability gain at this scale.

resource "aws_security_group" "lb" {
  name        = "${var.project_name}-internal-lb-sg"
  description = "Internal NLB fronting CatalogService/PictureService for the BFF Lambda (task 5.4). No path from the public internet."
  vpc_id      = var.vpc_id

  dynamic "ingress" {
    for_each = { for target in var.targets : target.name => target }
    content {
      description = "${ingress.value.name} listener, from inside the VPC only"
      from_port   = ingress.value.listener_port
      to_port     = ingress.value.listener_port
      protocol    = "tcp"
      cidr_blocks = [var.ingress_cidr]
    }
  }

  egress {
    description = "To the Fargate tasks security groups (see modules/fargate-service)"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, { Name = "${var.project_name}-internal-lb-sg" })
}

resource "aws_lb" "internal" {
  name               = "${var.project_name}-internal-lb"
  internal           = true
  load_balancer_type = "network"
  subnets            = var.private_subnet_ids
  security_groups    = [aws_security_group.lb.id]

  enable_cross_zone_load_balancing = true

  tags = var.tags
}

resource "aws_lb_target_group" "this" {
  for_each = { for target in var.targets : target.name => target }

  # AWS caps target group names at 32 characters — "${var.project_name}-..."
  # (e.g. "ninjago-scanner-picture-service-tg") would exceed that, so this
  # drops the project-name prefix other resources use; still unique enough
  # within a single-project AWS account.
  name        = "${each.value.name}-tg"
  port        = each.value.target_port
  protocol    = "TCP"
  vpc_id      = var.vpc_id
  target_type = "ip"

  # HTTP health check over the same TCP data-plane port: both services
  # expose a plain GET "/" (see CatalogService/PictureService Program.cs —
  # "This service exposes card catalog/photo scanning via gRPC...") which
  # Kestrel serves over HTTP/1.1 on the same port as the gRPC (HTTP/2)
  # traffic, so it works as a lightweight liveness probe without either
  # service needing a dedicated health endpoint.
  health_check {
    protocol            = "HTTP"
    path                = each.value.health_check_path
    port                = tostring(each.value.target_port)
    healthy_threshold   = 3
    unhealthy_threshold = 3
    interval            = 30
  }

  tags = var.tags
}

resource "aws_lb_listener" "this" {
  for_each = { for target in var.targets : target.name => target }

  load_balancer_arn = aws_lb.internal.arn
  port               = each.value.listener_port
  protocol           = "TCP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.this[each.key].arn
  }
}
