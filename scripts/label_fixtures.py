"""One-time helper: runs the REAL Gemini analysis pipeline over testdata/photos/ and writes the
results to testdata/sidecars.json, which is then hand-curated (edge-case rows) and committed.
Not part of `dev.ps1 up` - fixtures are labelled once, not on every start.

Needs CatalogService running (http://localhost:5073, or CATALOG_SERVICE_ADDRESS) and a Gemini key
(GEMINI_API_KEY in the environment, or in picture_service/.env - loaded here without printing it).

Usage (from repo root):
    uv run --project picture_service python scripts/label_fixtures.py
"""

from __future__ import annotations

import asyncio
import dataclasses
import json
import os
from datetime import datetime
from pathlib import Path

from picture_service import card_analysis, catalog_client
from picture_service.card_analysis_stage_1_and_2 import build_chat_model
from picture_service.config import ScannerConfig
from picture_service.models import SidecarRecord

REPO_ROOT = Path(__file__).resolve().parent.parent
PHOTOS = REPO_ROOT / "testdata" / "photos"
OUTPUT = REPO_ROOT / "testdata" / "sidecars.json"


def _load_dotenv(path: Path) -> None:
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#") and "=" in line:
            key, _, value = line.partition("=")
            os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


def _json_default(value):
    if isinstance(value, datetime):
        return value.isoformat()
    if isinstance(value, tuple):
        return list(value)
    raise TypeError(f"Cannot serialize {type(value)}")


async def main() -> None:
    _load_dotenv(REPO_ROOT / "picture_service" / ".env")
    config = ScannerConfig.load_for_upload()
    if not config.api_key:
        raise SystemExit("GEMINI_API_KEY is not set (environment or picture_service/.env).")

    catalog = await catalog_client.load_catalog_snapshot(config.catalog_service_address)

    sidecars: dict[str, dict] = {}
    for photo in sorted(PHOTOS.glob("*.jpg")):
        photo_id = photo.stem
        result = await card_analysis.analyze_card(
            build_chat_model, config, catalog, photo_id, photo.name, photo.read_bytes()
        )
        record = SidecarRecord.from_analysis_result(result)
        sidecars[photo_id] = dataclasses.asdict(record)
        print(f"{photo_id}: {record.analysis_status} {record.set_name} #{record.card_number} ({record.card_name})")

    OUTPUT.write_text(
        json.dumps(sidecars, indent=2, ensure_ascii=False, default=_json_default) + "\n", encoding="utf-8"
    )
    print(f"Wrote {OUTPUT}")


if __name__ == "__main__":
    asyncio.run(main())
