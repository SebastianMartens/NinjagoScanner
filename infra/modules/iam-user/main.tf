# Static IAM user for PictureService's cross-cloud access on Fly.io (see
# openspec/changes/fly-hosting-migration/design.md Decision 4). Fly.io compute has no AWS-native
# way to assume a role the way ECS's task role did, so PictureService authenticates with a
# long-lived access key instead — a real security posture downgrade vs. auto-rotated STS
# credentials, accepted as a pragmatic trade-off for a personal-scale app. The policy below is
# scoped as tightly as that trade-off allows: exactly the S3/DynamoDB actions PictureService's own
# code calls (see NinjagoScanner.PictureService/PhotoStore.cs and SidecarTable.cs), on exactly the
# photo bucket's `photos/*` prefix and the sidecar table — nothing else, and no other service gets
# an IAM user at all.

data "aws_iam_policy_document" "picture_service" {
  statement {
    sid    = "PhotoObjectAccess"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
    ]
    resources = ["${var.photo_bucket_arn}/photos/*"]
  }

  statement {
    sid    = "PhotoBucketListing"
    effect = "Allow"
    actions = [
      "s3:ListBucket",
    ]
    resources = [var.photo_bucket_arn]
    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["photos/*"]
    }
  }

  statement {
    sid    = "SidecarTableAccess"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:DeleteItem",
      "dynamodb:Scan",
    ]
    resources = [
      var.sidecar_table_arn,
      "${var.sidecar_table_arn}/index/*",
    ]
  }
}

resource "aws_iam_user" "picture_service" {
  name = "${var.project_name}-picture-service"
  path = "/${var.project_name}/"

  tags = var.tags
}

resource "aws_iam_user_policy" "picture_service" {
  name   = "${var.project_name}-picture-service-storage-access"
  user   = aws_iam_user.picture_service.name
  policy = data.aws_iam_policy_document.picture_service.json
}

# Terraform-managed so the secret never has to be typed/pasted by hand — `terraform output
# -raw picture_service_secret_access_key` reads it once, to set as a Fly secret (see
# infra/README.md). Stored in Terraform state like any other resource attribute; state already
# lives encrypted in the S3 backend (see modules/state-backend), consistent with how this repo
# already treats the Gemini API key (a Fly/user-secret, never committed).
resource "aws_iam_access_key" "picture_service" {
  user = aws_iam_user.picture_service.name
}
