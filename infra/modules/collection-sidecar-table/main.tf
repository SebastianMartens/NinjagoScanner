# DynamoDB table for sidecar records, keyed by Collection ID + generated Photo ID (see
# openspec/changes/add-collection-data-isolation/design.md). This REPLACES modules/sidecar-table's
# table (which used PhotoId alone as its hash key) as PictureService's live sidecar store, but is
# defined as its own separate resource/module rather than an edit to that one: DynamoDB's hash key
# is immutable, so changing modules/sidecar-table's hash_key in place would make Terraform plan a
# destroy-and-recreate of the *existing* table — deleting every sidecar record before the
# add-collection-data-isolation migration tool ever got a chance to copy it. This module creates a
# brand new table instead, leaving the old one (and its data) completely untouched until the
# migration is verified and modules/sidecar-table is deliberately decommissioned as a follow-up.
#
# No GSIs: modules/sidecar-table's three GSIs (ReviewStatusIndex, AnalysisStatusIndex,
# SeriesCardIndex) are not queried by any current PictureService code (confirmed via a repo-wide
# search for their names) - carried-over dead infrastructure from an earlier design, not
# reproduced here. Add one back if/when actual code needs it.

resource "aws_dynamodb_table" "collection_sidecars" {
  name         = "${var.project_name}-collection-sidecars"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "CollectionId"
  range_key    = "PhotoId"

  attribute {
    name = "CollectionId"
    type = "S"
  }

  attribute {
    name = "PhotoId"
    type = "S"
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
