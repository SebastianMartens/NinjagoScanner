# One ECR repository holding one service's container image (task 5.1's
# Dockerfiles build CatalogService and PictureService images; task 5.2
# instantiates this module once per service — see environments/prod/main.tf).
# Image scanning on push, and a lifecycle policy bounding storage growth
# from untagged/superseded images; no cross-account replication or tag
# immutability requirement for a personal-scale project.

resource "aws_ecr_repository" "this" {
  name                 = var.repository_name
  image_tag_mutability = "MUTABLE"

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = var.tags
}

resource "aws_ecr_lifecycle_policy" "this" {
  repository = aws_ecr_repository.this.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images after ${var.untagged_image_expiry_days} days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = var.untagged_image_expiry_days
        }
        action = { type = "expire" }
      },
      {
        # tagPatternList (rather than tagPrefixList) matches any tag
        # regardless of the scheme the future CI deploy workflow (task
        # 10.2/10.3, not built yet) ends up using for tags (commit SHA,
        # "latest", semver, ...).
        rulePriority = 2
        description  = "Keep only the most recent ${var.max_tagged_image_count} tagged images"
        selection = {
          tagStatus      = "tagged"
          tagPatternList = ["*"]
          countType      = "imageCountMoreThan"
          countNumber    = var.max_tagged_image_count
        }
        action = { type = "expire" }
      }
    ]
  })
}
