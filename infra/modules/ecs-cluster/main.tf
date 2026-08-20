# ECS cluster shared by CatalogService and PictureService (task 5.2), plus
# the Cloud Map "HTTP" namespace ECS Service Connect uses for internal
# service-to-service gRPC discovery (task 5.3 — PictureService reaching
# CatalogService by internal DNS name; see CatalogGrpcClient.cs /
# ScannerConfig.cs's CatalogService:Address).
#
# This is an HTTP namespace (aws_service_discovery_http_namespace), not a
# DNS namespace: Service Connect resolves names via the Envoy-based proxy
# sidecar ECS injects into each participating task's network namespace, not
# via real DNS records anywhere — so only tasks that are themselves part of
# a Service-Connect-enabled ECS service can resolve these names at all. This
# matters for task 5.4: the Web BFF Lambda (task 9, not built yet) is not an
# ECS task and never gets that sidecar, so it cannot reach either service
# through this namespace no matter how it's configured — see
# modules/internal-lb for how the BFF reaches these services instead, and
# infra/README.md for the full writeup of that conclusion.

resource "aws_service_discovery_http_namespace" "service_connect" {
  name        = "${var.project_name}.internal"
  description = "ECS Service Connect namespace for CatalogService/PictureService internal gRPC discovery."

  tags = var.tags
}

resource "aws_ecs_cluster" "main" {
  name = "${var.project_name}-cluster"

  setting {
    name  = "containerInsights"
    value = var.container_insights_enabled ? "enabled" : "disabled"
  }

  service_connect_defaults {
    namespace = aws_service_discovery_http_namespace.service_connect.arn
  }

  tags = var.tags
}
