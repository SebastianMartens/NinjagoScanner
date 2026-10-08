"""Copies an evenly spaced sample of card photos from cardFotos/ into testdata/photos/,
downscaled to a max edge of 800px (fixture photos only need to look right, not be full size).

Usage (from repo root):
    uv run --project picture_service --with pillow python scripts/pick_fixtures.py [--count 24]

Photo IDs in the fixtures are the file stems (fixture-01 ... fixture-NN), so the committed
files carry no personal file names.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageOps

REPO_ROOT = Path(__file__).resolve().parent.parent
SOURCE = REPO_ROOT / "cardFotos"
TARGET = REPO_ROOT / "testdata" / "photos"
MAX_EDGE = 800


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--count", type=int, default=24)
    args = parser.parse_args()

    sources = sorted(SOURCE.glob("*.jpg"))
    if len(sources) < args.count:
        raise SystemExit(f"Only {len(sources)} photos in {SOURCE}, need {args.count}.")

    step = len(sources) / args.count
    picked = [sources[int(i * step)] for i in range(args.count)]

    TARGET.mkdir(parents=True, exist_ok=True)
    for index, source in enumerate(picked, start=1):
        with Image.open(source) as image:
            image = ImageOps.exif_transpose(image)
            image.thumbnail((MAX_EDGE, MAX_EDGE))
            image.convert("RGB").save(TARGET / f"fixture-{index:02d}.jpg", quality=82, optimize=True)
        print(f"fixture-{index:02d}.jpg <- {source.name}")


if __name__ == "__main__":
    main()
