# DynamoDB table for sidecar records (replaces PictureService's per-photo
# JSON sidecar files — see design.md "Storage backend" and
# openspec/GLOSSARY.md's Sidecar / Analysis Status / Review Status / Owned
# Copies entries).
#
# Partition key: PhotoId, a generated identifier (GUID/ULID, assigned at
# upload time — task 3.1) that replaces the original filename as identity.
# The original filename is retained only as a plain (non-indexed) attribute
# (SourceFileName), matching design.md's "Photo identity" decision.
#
# This app manages exactly one shared photo collection today (cardFotos/) —
# design.md describes no multi-collection/tenant concept. So there is no
# per-collection partitioning attribute in this schema; if that ever
# changes, a CollectionId attribute and a matching GSI partition key would
# need to be added then. Not built ahead of that actual need.
#
# GSIs, one per real query the app makes against sidecar records today:
#   - ReviewStatusIndex:   list-by-review-status  (the Review page's filter)
#   - AnalysisStatusIndex: list-by-analysis-status (Overview / scan status)
#   - SeriesCardIndex:     lookup-by-series+card-number — this is how the
#     app computes "Owned Copies" (GLOSSARY.md): querying
#     SeriesName = X AND CardNumber = Y returns every sidecar matching that
#     catalog card; zero results means the card is missing, more than one
#     means a duplicate. The index is intentionally non-unique per
#     (SeriesName, CardNumber) pair — that's what makes the duplicate count
#     possible in the first place.
# All three GSIs use ScannedAtUtc as their sort key, so each query naturally
# comes back ordered by scan time. Projection is ALL rather than KEYS_ONLY:
# the table is small (thousands of items, not millions), and every one of
# these queries wants the full sidecar record back, not just the key
# attributes plus a second read.
#
# Naming note: PictureService's current C# code calls the series field
# `SetName` (see ScannerModels.cs / SidecarStore.cs), while the catalog
# service's own field and openspec/GLOSSARY.md both call it "Series Name" /
# series_name. This table uses the domain name `SeriesName` as the
# canonical attribute; task 3's storage-layer rewrite is the natural point
# to reconcile the C# property name to match.

resource "aws_dynamodb_table" "sidecars" {
  name         = "${var.project_name}-sidecars"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "PhotoId"

  attribute {
    name = "PhotoId"
    type = "S"
  }

  attribute {
    name = "ReviewStatus"
    type = "S"
  }

  attribute {
    name = "AnalysisStatus"
    type = "S"
  }

  attribute {
    name = "SeriesName"
    type = "S"
  }

  attribute {
    name = "CardNumber"
    type = "S"
  }

  attribute {
    name = "ScannedAtUtc"
    type = "S"
  }

  global_secondary_index {
    name            = "ReviewStatusIndex"
    hash_key        = "ReviewStatus"
    range_key       = "ScannedAtUtc"
    projection_type = "ALL"
  }

  global_secondary_index {
    name            = "AnalysisStatusIndex"
    hash_key        = "AnalysisStatus"
    range_key       = "ScannedAtUtc"
    projection_type = "ALL"
  }

  global_secondary_index {
    name            = "SeriesCardIndex"
    hash_key        = "SeriesName"
    range_key       = "CardNumber"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }

  deletion_protection_enabled = var.deletion_protection_enabled

  tags = var.tags
}
