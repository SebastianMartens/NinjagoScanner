"""Creates the S3 bucket and DynamoDB table in the local moto server and loads the fixtures
(testdata/photos + testdata/sidecars.json, assigned to collections by testdata/manifest.json).

Reuses picture_service's own PhotoStore/SidecarTable so the items and keys written are exactly
what the service reads. Always starts from a clean slate (deletes and recreates the bucket and
table), so running it is also the `reset` for the AWS side.

Refuses to run unless AWS_ENDPOINT_URL is set, so it can never write into real AWS.

Usage (dev.ps1 does this; from repo root):
    uv run --project picture_service python scripts/seed_moto.py
"""

from __future__ import annotations

import asyncio
import json
import os
from datetime import datetime
from pathlib import Path

import aioboto3
from botocore.exceptions import ClientError

from picture_service.models import SidecarRecord
from picture_service.photo_store import PhotoStore
from picture_service.sidecar_table import SidecarTable

REPO_ROOT = Path(__file__).resolve().parent.parent
TESTDATA = REPO_ROOT / "testdata"


def _record_from_json(raw: dict) -> SidecarRecord:
    values = dict(raw)
    if values.get("scanned_at_utc"):
        values["scanned_at_utc"] = datetime.fromisoformat(values["scanned_at_utc"])
    if values.get("detected_text") is not None:
        values["detected_text"] = tuple(values["detected_text"])
    return SidecarRecord(**values)


async def _recreate_bucket(s3, bucket: str, region: str) -> None:
    try:
        listing = await s3.list_objects_v2(Bucket=bucket)
        for obj in listing.get("Contents", []):
            await s3.delete_object(Bucket=bucket, Key=obj["Key"])
        await s3.delete_bucket(Bucket=bucket)
    except ClientError as error:
        if error.response["Error"]["Code"] not in ("NoSuchBucket", "404"):
            raise
    await s3.create_bucket(Bucket=bucket, CreateBucketConfiguration={"LocationConstraint": region})


async def _recreate_table(dynamodb, table_name: str) -> None:
    try:
        existing = await dynamodb.Table(table_name)
        await existing.delete()
        await existing.wait_until_not_exists()
    except ClientError as error:
        if error.response["Error"]["Code"] != "ResourceNotFoundException":
            raise
    # Same key schema as infra/modules/collection-sidecar-table.
    table = await dynamodb.create_table(
        TableName=table_name,
        BillingMode="PAY_PER_REQUEST",
        KeySchema=[
            {"AttributeName": "CollectionId", "KeyType": "HASH"},
            {"AttributeName": "PhotoId", "KeyType": "RANGE"},
        ],
        AttributeDefinitions=[
            {"AttributeName": "CollectionId", "AttributeType": "S"},
            {"AttributeName": "PhotoId", "AttributeType": "S"},
        ],
    )
    await table.wait_until_exists()


async def main() -> None:
    endpoint_url = os.environ.get("AWS_ENDPOINT_URL")
    if not endpoint_url:
        raise SystemExit("AWS_ENDPOINT_URL is not set - refusing to seed (this must only target local moto).")

    region = os.environ.get("AWS_REGION", "eu-central-1")
    bucket = os.environ.get("PHOTOS_BUCKET_NAME", "ninjago-local-photos")
    table_name = os.environ.get("SIDECAR_TABLE_NAME", "ninjago-local-sidecars")

    manifest = json.loads((TESTDATA / "manifest.json").read_text(encoding="utf-8"))
    sidecars = json.loads((TESTDATA / "sidecars.json").read_text(encoding="utf-8"))

    session = aioboto3.Session(region_name=region)
    async with session.client("s3", endpoint_url=endpoint_url) as s3, session.resource(
        "dynamodb", endpoint_url=endpoint_url
    ) as dynamodb:
        await _recreate_bucket(s3, bucket, region)
        await _recreate_table(dynamodb, table_name)

        photo_store = PhotoStore(s3, bucket)
        sidecar_table = SidecarTable(dynamodb, table_name)

        count = 0
        for user in manifest["users"]:
            for photo_id in user["photoIds"]:
                photo_bytes = (TESTDATA / "photos" / f"{photo_id}.jpg").read_bytes()
                await photo_store.put_bytes(user["collectionId"], photo_id, photo_bytes)
                await sidecar_table.put(user["collectionId"], photo_id, _record_from_json(sidecars[photo_id]))
                count += 1

    print(f"Seeded {count} photos into bucket '{bucket}' / table '{table_name}' at {endpoint_url}.")


if __name__ == "__main__":
    asyncio.run(main())
