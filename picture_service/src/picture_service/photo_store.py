"""S3-backed photo byte storage, ported from PhotoStore.cs.

The only module that ever touches photo bytes or AWS S3 credentials directly - Web
streams upload bytes to this service over gRPC and fetches display images via the
pre-signed download URLs this module creates.
"""

from __future__ import annotations

import asyncio
from collections.abc import AsyncIterator
from datetime import timedelta
from typing import Any

from botocore.exceptions import ClientError

_KEY_PREFIX = "photos/"
_DOWNLOAD_URL_LIFETIME = timedelta(hours=1)


def build_object_key(collection_id: str, photo_id: str) -> str:
    return f"{_KEY_PREFIX}{collection_id}/{photo_id}"


def _build_collection_prefix(collection_id: str) -> str:
    return f"{_KEY_PREFIX}{collection_id}/"


class PhotoStore:
    def __init__(self, s3_client: Any, bucket_name: str) -> None:
        """`s3_client` is an already-open aioboto3 S3 client, held for the caller's lifetime
        (see main.py's `_serve`) rather than opened fresh per call - a fresh client per call
        was the picture-service-aws-client-reuse regression this replaced."""
        self._s3 = s3_client
        self._bucket_name = bucket_name

    async def put_bytes(self, collection_id: str, photo_id: str, data: bytes) -> None:
        await self._s3.put_object(
            Bucket=self._bucket_name,
            Key=build_object_key(collection_id, photo_id),
            Body=data,
        )

    async def create_download_url(self, collection_id: str, photo_id: str) -> str:
        """`generate_presigned_url` does no network I/O - it's local HMAC signing - but
        aiobotocore still exposes it as a coroutine, and in practice that coroutine runs
        to completion without ever yielding, so a plain `await` blocks PictureService's
        single event loop for the duration of the signing work. Running it via
        `asyncio.run` inside `asyncio.to_thread` moves that work off the loop entirely."""

        def _presign_in_thread() -> str:
            return asyncio.run(
                self._s3.generate_presigned_url(
                    "get_object",
                    Params={
                        "Bucket": self._bucket_name,
                        "Key": build_object_key(collection_id, photo_id),
                    },
                    ExpiresIn=int(_DOWNLOAD_URL_LIFETIME.total_seconds()),
                )
            )

        return await asyncio.to_thread(_presign_in_thread)

    async def get_bytes(self, collection_id: str, photo_id: str) -> bytes:
        response = await self._s3.get_object(
            Bucket=self._bucket_name,
            Key=build_object_key(collection_id, photo_id),
        )
        async with response["Body"] as stream:
            return await stream.read()

    async def exists(self, collection_id: str, photo_id: str) -> bool:
        try:
            await self._s3.head_object(
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
        await self._s3.delete_object(
            Bucket=self._bucket_name,
            Key=build_object_key(collection_id, photo_id),
        )

    async def list_photo_ids(self, collection_id: str) -> AsyncIterator[str]:
        prefix = _build_collection_prefix(collection_id)
        paginator = self._s3.get_paginator("list_objects_v2")
        async for page in paginator.paginate(Bucket=self._bucket_name, Prefix=prefix):
            for entry in page.get("Contents", []):
                yield entry["Key"][len(prefix) :]
