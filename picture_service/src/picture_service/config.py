"""Layered configuration resolution, ported from ScannerConfig.cs.

Resolution order per setting: an explicit per-request override (from a ScanRequest or
UploadPhotoMetadata proto message) -> a config key -> an environment variable -> a
hardcoded default.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

SUPPORTED_EXTENSIONS = frozenset({".jpg", ".jpeg", ".png", ".bmp", ".webp"})

_DEFAULT_MODEL = "gemini-3.1-flash-lite"
_DEFAULT_CATALOG_SERVICE_ADDRESS = "http://localhost:5073"
_DEFAULT_DELAY_BETWEEN_REQUESTS_MS = 0
_DEFAULT_MAX_ATTEMPTS = 3
_DEFAULT_TIMEOUT_SECONDS = 90


def _env(*names: str) -> str | None:
    for name in names:
        value = os.environ.get(name)
        if value is not None:
            return value
    return None


def _try_parse_int(value: str | None, fallback: int) -> int:
    if value is None:
        return fallback
    try:
        return int(value)
    except ValueError:
        return fallback


def _try_parse_bool(value: str | None) -> bool:
    return value is not None and value.strip().lower() in ("true", "1")


@dataclass(frozen=True)
class ScannerConfig:
    api_key: str
    model: str
    catalog_service_address: str
    overwrite_existing_sidecars: bool
    delay_between_requests_ms: int
    max_attempts: int
    timeout_seconds: int

    @staticmethod
    def load_for_scan(
        *,
        api_key: str | None = None,
        model: str | None = None,
        catalog_service_address: str | None = None,
        overwrite_existing_sidecars: bool | None = None,
        delay_between_requests_ms: int | None = None,
        max_attempts: int | None = None,
        timeout_seconds: int | None = None,
    ) -> ScannerConfig:
        return ScannerConfig(
            api_key=api_key or _env("GEMINI_API_KEY") or "",
            model=model or _env("GEMINI_MODEL") or _DEFAULT_MODEL,
            catalog_service_address=catalog_service_address
            or _env("CATALOG_SERVICE_ADDRESS")
            or _DEFAULT_CATALOG_SERVICE_ADDRESS,
            overwrite_existing_sidecars=(
                overwrite_existing_sidecars
                if overwrite_existing_sidecars is not None
                else _try_parse_bool(_env("OVERWRITE_SIDECARS"))
            ),
            delay_between_requests_ms=(
                delay_between_requests_ms
                if delay_between_requests_ms is not None
                else _try_parse_int(_env("DELAY_BETWEEN_REQUESTS_MS"), _DEFAULT_DELAY_BETWEEN_REQUESTS_MS)
            ),
            max_attempts=max(
                1,
                max_attempts
                if max_attempts is not None
                else _try_parse_int(_env("MAX_ATTEMPTS"), _DEFAULT_MAX_ATTEMPTS),
            ),
            timeout_seconds=max(
                10,
                timeout_seconds
                if timeout_seconds is not None
                else _try_parse_int(_env("HTTP_TIMEOUT_SECONDS"), _DEFAULT_TIMEOUT_SECONDS),
            ),
        )

    @staticmethod
    def load_for_upload(
        *,
        api_key: str | None = None,
        model: str | None = None,
        catalog_service_address: str | None = None,
        max_attempts: int | None = None,
        timeout_seconds: int | None = None,
    ) -> ScannerConfig:
        return ScannerConfig(
            api_key=api_key or _env("GEMINI_API_KEY") or "",
            model=model or _env("GEMINI_MODEL") or _DEFAULT_MODEL,
            catalog_service_address=catalog_service_address
            or _env("CATALOG_SERVICE_ADDRESS")
            or _DEFAULT_CATALOG_SERVICE_ADDRESS,
            overwrite_existing_sidecars=True,
            delay_between_requests_ms=0,
            max_attempts=max(
                1,
                max_attempts
                if max_attempts is not None
                else _try_parse_int(_env("MAX_ATTEMPTS"), _DEFAULT_MAX_ATTEMPTS),
            ),
            timeout_seconds=max(
                10,
                timeout_seconds
                if timeout_seconds is not None
                else _try_parse_int(_env("HTTP_TIMEOUT_SECONDS"), _DEFAULT_TIMEOUT_SECONDS),
            ),
        )


def resolve_photos_bucket_name() -> str:
    """The S3 bucket holding card photos, keyed by generated photo ID (see photo_store.py)."""
    value = _env("PHOTOS_BUCKET_NAME")
    if not value:
        raise RuntimeError(
            "PHOTOS_BUCKET_NAME must be configured - the S3 bucket for card photos."
        )
    return value


def resolve_sidecar_table_name() -> str:
    """The DynamoDB table holding sidecar records, keyed by generated photo ID (see sidecar_table.py)."""
    value = _env("SIDECAR_TABLE_NAME")
    if not value:
        raise RuntimeError(
            "SIDECAR_TABLE_NAME must be configured - the DynamoDB table for sidecar records."
        )
    return value


def resolve_aws_region() -> str:
    """The AWS region for S3/DynamoDB. botocore only reads AWS_DEFAULT_REGION by default, not
    AWS_REGION (the name the C# version used via AWS__Region), so this must be passed explicitly
    to aioboto3.Session(region_name=...) rather than left to botocore's own env var lookup."""
    value = _env("AWS_REGION", "AWS_DEFAULT_REGION")
    if not value:
        raise RuntimeError("AWS_REGION must be configured - the AWS region for S3/DynamoDB.")
    return value
