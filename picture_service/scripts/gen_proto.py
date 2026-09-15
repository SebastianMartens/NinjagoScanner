"""Generates gRPC Python stubs from the shared .proto files.

Run via `uv run python scripts/gen_proto.py` from `picture_service/` before running the
service or its tests. Output lands in `src/picture_service/_generated/` and is not
committed - the .proto files are the single source of truth (mirrors Grpc.Tools' role
for the C# projects).
"""

import re
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = Path(__file__).resolve().parent.parent / "src" / "picture_service" / "_generated"

PROTO_SOURCES = [
    (REPO_ROOT / "NinjagoScanner.Web" / "Protos", "picture_service.proto"),
    (REPO_ROOT / "NinjagoScanner.CatalogService" / "Protos", "catalog.proto"),
]


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    init_file = OUT_DIR / "__init__.py"
    if not init_file.exists():
        init_file.write_text("", encoding="utf-8")

    for proto_dir, filename in PROTO_SOURCES:
        subprocess.run(
            [
                sys.executable,
                "-m",
                "grpc_tools.protoc",
                f"-I{proto_dir}",
                f"--python_out={OUT_DIR}",
                f"--grpc_python_out={OUT_DIR}",
                f"--pyi_out={OUT_DIR}",
                str(proto_dir / filename),
            ],
            check=True,
            cwd=REPO_ROOT,
        )

    _fix_relative_imports()
    print(f"Generated proto stubs in {OUT_DIR}")


def _fix_relative_imports() -> None:
    """grpc_tools.protoc emits bare `import x_pb2 as y` in generated *_grpc.py files, which
    only resolves when the generated directory itself is on sys.path. Rewrite those to
    explicit relative imports so the stubs work as a normal subpackage instead.
    """
    pattern = re.compile(r"^import (\w+_pb2) as (\w+)$", re.MULTILINE)
    for path in OUT_DIR.glob("*_pb2_grpc.py"):
        text = path.read_text(encoding="utf-8")
        fixed = pattern.sub(r"from . import \1 as \2", text)
        if fixed != text:
            path.write_text(fixed, encoding="utf-8")


if __name__ == "__main__":
    main()
