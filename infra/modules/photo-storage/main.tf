# S3 bucket for card photo storage (replaces PictureService's local
# cardFotos/ disk — see design.md "Storage backend"). Photo bytes only;
# sidecar metadata (analysis status, review status, card match, Gemini
# output) lives in DynamoDB — see ../sidecar-table.

data "aws_caller_identity" "current" {}

resource "aws_s3_bucket" "photos" {
  bucket = "${var.project_name}-photos-${data.aws_caller_identity.current.account_id}"

  tags = var.tags
}

resource "aws_s3_bucket_versioning" "photos" {
  bucket = aws_s3_bucket.photos.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "photos" {
  bucket = aws_s3_bucket.photos.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

resource "aws_s3_bucket_public_access_block" "photos" {
  bucket = aws_s3_bucket.photos.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# design.md's upload flow: "the BFF issues a short-lived pre-authorized
# upload URL; the browser uploads photo bytes straight to S3, never through
# the BFF/API Gateway." The bucket itself has to allow cross-origin PUTs for
# that to work from a browser; issuing the presigned URL is the BFF's job
# (task 7.4, out of scope here).
resource "aws_s3_bucket_cors_configuration" "photos" {
  bucket = aws_s3_bucket.photos.id

  cors_rule {
    allowed_methods = ["PUT", "GET", "HEAD"]
    allowed_origins = var.cors_allowed_origins
    allowed_headers = ["*"]
    expose_headers  = ["ETag"]
    max_age_seconds = 3000
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "photos" {
  bucket = aws_s3_bucket.photos.id

  rule {
    id     = "abort-incomplete-multipart-uploads"
    status = "Enabled"

    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }

    filter {}
  }

  rule {
    id     = "expire-noncurrent-versions"
    status = "Enabled"

    # Versioning here is a safety net against accidental overwrite/delete,
    # not a full history feature the app needs — old versions are pruned
    # after a month rather than kept forever.
    noncurrent_version_expiration {
      noncurrent_days = 30
    }

    filter {}
  }
}
