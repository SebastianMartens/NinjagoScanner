"""S3-backed photo byte storage, ported from PhotoStore.cs.

The only module that ever touches photo bytes or AWS S3 credentials directly - Web
streams upload bytes to this service over gRPC and fetches display images via the
pre-signed download URLs this module creates.
"""

from __future__ import annotations

from collections.abc import AsyncIterator
from datetime import timedelta

import aioboto3
from botocore.exceptions import ClientError

_KEY_PREFIX = "photos/"
_DOWNLOAD_URL_LIFETIME = timedelta(hours=1)


def build_object_key(collection_id: str, photo_id: str) -> str:
    return f"{_KEY_PREFIX}{collection_id}/{photo_id}"


def _build_collection_prefix(collection_id: str) -> str:
    return f"{_KEY_PREFIX}{collection_id}/"


class PhotoStore:
    def __init__(self, session: aioboto3.Session, bucket_name: str, *, endpoint_url: str | None = None) -> None:
        self._session = session
        self._bucket_name = bucket_name
        self._endpoint_url = endpoint_url

    def _client(self):
        return self._session.client("s3", endpoint_url=self._endpoint_url)

    async def put_bytes(self, collection_id: str, photo_id: str, data: bytes) -> None:
        async with self._client() as s3:
            await s3.put_object(
                Bucket=self._bucket_name,
                Key=build_object_key(collection_id, photo_id),
                Body=data,
            )

    async def create_download_url(self, collection_id: str, photo_id: str) -> str:
        async with self._client() as s3:
            return await s3.generate_presigned_url(
                "get_object",
                Params={
                    "Bucket": self._bucket_name,
                    "Key": build_object_key(collection_id, photo_id),
                },
                ExpiresIn=int(_DOWNLOAD_URL_LIFETIME.total_seconds()),
            )

    async def get_bytes(self, collection_id: str, photo_id: str) -> bytes:
        async with self._client() as s3:
            response = await s3.get_object(
                Bucket=self._bucket_name,
                Key=build_object_key(collection_id, photo_id),
            )
            async with response["Body"] as stream:
                return await stream.read()

    async def exists(self, collection_id: str, photo_id: str) -> bool:
        async with self._client() as s3:
            try:
                await s3.head_object(
                    Bucket=self._bucket_name,
                    Key=build_object_key(collection_id, photo_id),
                )
                return True
            except ClientError as exception:
                status_code = exception.response.get("ResponseMetadata", {}).get("HTTPStatusCode")
                if status_code == 404 or exception.response.get("Error", {}).get("Code") == "404":
                    return False
                raise

    async def delete(self, collection_id: str, photo_id: str) -> None:
        async with self._client() as s3:
            await s3.delete_object(
                Bucket=self._bucket_name,
                Key=build_object_key(collection_id, photo_id),
            )

    async def list_photo_ids(self, collection_id: str) -> AsyncIterator[str]:
        prefix = _build_collection_prefix(collection_id)
        async with self._client() as s3:
            paginator = s3.get_paginator("list_objects_v2")
            async for page in paginator.paginate(Bucket=self._bucket_name, Prefix=prefix):
                for entry in page.get("Contents", []):
                    yield entry["Key"][len(prefix) :]
