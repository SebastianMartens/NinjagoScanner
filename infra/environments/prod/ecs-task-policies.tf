# IAM policy for PictureService's ECS *task* role — used by the application
# code inside the container via the AWS SDK (see modules/fargate-service's
# header comment for the execution-role/task-role split). Scoped to exactly
# what PictureService's current C# code calls, verified against source
# rather than assumed from design.md's prose:
#
#   - NinjagoScanner.PictureService/PhotoStore.cs calls GetObjectAsync,
#     GetObjectMetadataAsync (ExistsAsync), DeleteObjectAsync, and
#     ListObjectsV2Async — never PutObjectAsync anywhere in this file or
#     elsewhere in the service. Uploads go browser-to-S3 directly via a
#     pre-authorized URL the Web BFF issues (design.md's "Upload flow"
#     decision, tasks 7.4/8.3) — PictureService itself never writes photo
#     bytes, only reads and deletes them. So this policy grants
#     s3:GetObject/s3:DeleteObject/s3:ListBucket and nothing else; no
#     s3:PutObject.
#   - NinjagoScanner.PictureService/SidecarTable.cs calls GetItemAsync,
#     PutItemAsync, DeleteItemAsync, and a full-table Scan (ListAllAsync
#     uses `table.Scan(new ScanOperationConfig())`) — there is no
#     dynamodb:Query against any GSI anywhere in the current code, despite
#     the table having three GSIs (see modules/sidecar-table). So this
#     policy grants only base-table actions (GetItem/PutItem/DeleteItem/
#     Scan) and deliberately does *not* include the GSI ARNs
#     ("${table_arn}/index/*") — add dynamodb:Query scoped to the specific
#     index ARN(s) only if/when the app is changed to query an index
#     instead of scanning the whole table.
#
# CatalogService needs no equivalent policy — it only reads local
# cardInfos/*.json baked into its own image (see CatalogRepository.cs), no
# AWS API calls anywhere in its code — so its module.catalog_service
# instantiation (main.tf) passes task_role_policy_json = null and gets no
# task role at all.
#
# Note: PictureService's Gemini API key/model are also not referenced here.
# GeminiApiService.cs/ScannerConfig.cs only read them back out of
# IConfiguration (i.e. the environment variables the ECS *execution* role
# already resolved from Secrets Manager at task startup, per
# modules/fargate-service) — the application code never calls the Secrets
# Manager SDK directly, so the task role needs no Secrets Manager
# permission at all.

data "aws_iam_policy_document" "picture_service_task" {
  statement {
    sid    = "PhotoBucketReadDelete"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:DeleteObject",
    ]
    resources = ["${module.photo_storage.bucket_arn}/photos/*"]
  }

  statement {
    sid       = "PhotoBucketList"
    effect    = "Allow"
    actions   = ["s3:ListBucket"]
    resources = [module.photo_storage.bucket_arn]

    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values   = ["photos/*"]
    }
  }

  statement {
    sid    = "SidecarTableReadWrite"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:DeleteItem",
      "dynamodb:Scan",
    ]
    resources = [module.sidecar_table.table_arn]
  }
}
