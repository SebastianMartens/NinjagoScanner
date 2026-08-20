# S3 + CloudFront for NinjagoScanner.Web.Client (the Blazor WASM static
# client) — task 9.2 — plus the CloudFront routing that also proxies the
# Web BFF's API Gateway endpoint from the same distribution (task 9.3), so
# the browser only ever talks to one origin and CORS between the client and
# the BFF never comes up at all.
#
# ---- Bucket: private, CloudFront-only access via Origin Access Control ----
# No public bucket policy, no static website hosting mode — Origin Access
# Control (OAC, the modern replacement for the older Origin Access Identity)
# signs CloudFront's requests to the bucket, and the bucket policy below
# grants access only to this specific distribution's ARN. Matches
# modules/photo-storage's "block all public access" posture even though
# these are two very different buckets (this one serves public content, but
# only ever through CloudFront, never via direct S3 URLs).
#
# ---- One distribution, two origins, no CORS ----
# design.md's Web split put the WASM client and the BFF on two independently
# deployable pipelines (S3+CloudFront vs. Lambda+API Gateway), but nothing
# requires them to be two different *origins* from the browser's point of
# view. This distribution serves the WASM client's static assets as its
# default cache behavior, and forwards anything under "/api/*" to the BFF's
# API Gateway as a second, uncached origin — see the ordered_cache_behavior
# block below. Because both are fronted by the same CloudFront domain, a
# request from the WASM client to "/api/series" is same-origin from the
# browser's perspective: no preflight, no Access-Control-Allow-Origin
# checks, nothing to configure. NinjagoScanner.Web.Client's default
# BffBaseAddress (wwwroot/appsettings.json) already relies on exactly this —
# it falls back to WebAssemblyHostBuilder's own HostEnvironment.BaseAddress
# (i.e. "call whatever origin I was served from") whenever no explicit
# BffBaseAddress is configured, so production simply doesn't need to set
# that value once this distribution is live.
#
# ---- SPA client-side routing: a CloudFront Function, not a 403/404 ->
# index.html error mapping ----
# Blazor WASM's router needs every path the app defines (e.g. "/collection",
# "/gallery/SomeSeries") to actually resolve to index.html so the SPA shell
# can boot and take over routing client-side, even though S3 has no object
# at that key. The commonly suggested fix is a CloudFront custom_error_response
# mapping 403/404 -> "/index.html" with response code 200. That was
# considered and rejected here: custom_error_response is *distribution-wide*,
# not scoped to one cache behavior — it would also intercept the BFF
# behavior's genuine 403/404s (e.g. CardCatalogService.DeletePhotoAsync's
# NotFound, or GetCollectionCardDetailsAsync returning Results.NotFound()),
# silently rewriting real API error responses into the HTML shell instead of
# the JSON the WASM client expects. Instead, this module attaches a
# CloudFront Function (viewer-request, sub-millisecond, effectively free) to
# *only* the default cache behavior: if the requested URI's last path
# segment has no "." (i.e. it doesn't look like a static file — no
# extension), rewrite it to "/index.html" before the request ever reaches
# S3. Because CloudFront picks a cache behavior by path pattern before
# running that behavior's function, "/api/*" requests never run this
# function at all — the BFF's real responses (including its real 404s) are
# never touched. A request for a genuinely missing static asset (has a dot,
# e.g. a stale hashed filename after a redeploy) still gets a plain 403/404,
# which is correct.

resource "aws_s3_bucket" "web_client" {
  bucket = "${var.project_name}-web-client-${var.aws_account_id}"

  tags = var.tags
}

resource "aws_s3_bucket_public_access_block" "web_client" {
  bucket = aws_s3_bucket.web_client.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "web_client" {
  bucket = aws_s3_bucket.web_client.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
    bucket_key_enabled = true
  }
}

# ---- CloudFront Origin Access Control ----

resource "aws_cloudfront_origin_access_control" "web_client" {
  name                              = "${var.project_name}-web-client-oac"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

data "aws_iam_policy_document" "web_client_bucket_policy" {
  statement {
    sid    = "AllowCloudFrontOAC"
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.web_client.arn}/*"]

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.web.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "web_client" {
  bucket = aws_s3_bucket.web_client.id
  policy = data.aws_iam_policy_document.web_client_bucket_policy.json
}

# ---- SPA fallback function (see header comment) ----

resource "aws_cloudfront_function" "spa_fallback" {
  name    = "${var.project_name}-spa-fallback"
  runtime = "cloudfront-js-2.0"
  # CloudFront function comments are capped at 128 characters; full
  # rationale is in this file's header comment above, not here.
  comment = "SPA fallback: rewrite extensionless paths to /index.html (default behavior only, never /api/*)"
  publish = true
  code    = <<-EOT
    function handler(event) {
      var request = event.request;
      var uri = request.uri;
      var lastSegment = uri.substring(uri.lastIndexOf('/') + 1);

      if (lastSegment.indexOf('.') === -1) {
        request.uri = '/index.html';
      }

      return request;
    }
  EOT
}

# ---- AWS-managed cache / origin-request policies ----
# Referenced by name (not hardcoded IDs) so this module doesn't silently
# break if AWS ever changes a policy's ID (names are stable, IDs aren't
# documented as such).

data "aws_cloudfront_cache_policy" "caching_optimized" {
  name = "Managed-CachingOptimized"
}

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

# The BFF's endpoints read query strings (e.g. GET /api/gallery?series=...,
# GET /api/collection/details?series=...&cardNumber=...) and JSON bodies on
# PUT/POST, so the API behavior needs everything forwarded — except the
# Host header, which CloudFront must be allowed to overwrite with the
# origin's own domain (the API Gateway execute-api domain), not the
# viewer's CloudFront domain. "AllViewer" (without "ExceptHostHeader")
# forwards the original Host header too, which breaks custom origins like
# API Gateway that route/validate on it — this is the AWS-documented reason
# "AllViewerExceptHostHeader" exists specifically for CloudFront-in-front-of
# -API-Gateway/ALB setups.
data "aws_cloudfront_origin_request_policy" "all_viewer_except_host" {
  name = "Managed-AllViewerExceptHostHeader"
}

# ---- Distribution ----

resource "aws_cloudfront_distribution" "web" {
  enabled             = true
  default_root_object = "index.html"
  price_class         = var.price_class
  comment             = "${var.project_name} — WASM client (default) + BFF API proxy under /api/* (task 9.3)"

  aliases = var.domain_aliases

  origin {
    origin_id                = "static-site"
    domain_name              = aws_s3_bucket.web_client.bucket_regional_domain_name
    origin_access_control_id = aws_cloudfront_origin_access_control.web_client.id
  }

  origin {
    origin_id   = "bff-api"
    domain_name = var.api_origin_domain_name

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id       = "static-site"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    compress               = true
    cache_policy_id        = data.aws_cloudfront_cache_policy.caching_optimized.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.spa_fallback.arn
    }
  }

  ordered_cache_behavior {
    path_pattern             = "/api/*"
    target_origin_id         = "bff-api"
    viewer_protocol_policy   = "https-only"
    allowed_methods          = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods           = ["GET", "HEAD"]
    compress                 = true
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = length(var.domain_aliases) == 0
    acm_certificate_arn            = length(var.domain_aliases) == 0 ? null : var.acm_certificate_arn
    ssl_support_method             = length(var.domain_aliases) == 0 ? null : "sni-only"
    minimum_protocol_version       = length(var.domain_aliases) == 0 ? null : "TLSv1.2_2021"
  }

  tags = var.tags
}
