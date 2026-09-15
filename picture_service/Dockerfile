# Build context must be the repository root, e.g.:
#   docker build -f picture_service/Dockerfile -t ninjago-picture-service .
# Needs NinjagoScanner.Web/Protos/picture_service.proto and
# NinjagoScanner.CatalogService/Protos/catalog.proto for proto codegen (scripts/gen_proto.py) -
# the .proto files are the only remaining coupling to the .NET side of the repo.

FROM ghcr.io/astral-sh/uv:python3.12-bookworm-slim AS build
WORKDIR /src

ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy

# Dependencies first (cache-friendly): a change to application code alone won't invalidate this
# layer.
COPY picture_service/pyproject.toml picture_service/uv.lock picture_service/
RUN cd picture_service && uv sync --frozen --no-install-project --no-dev

COPY picture_service/ picture_service/
COPY NinjagoScanner.Web/Protos/ NinjagoScanner.Web/Protos/
COPY NinjagoScanner.CatalogService/Protos/ NinjagoScanner.CatalogService/Protos/

RUN cd picture_service && uv sync --frozen --no-dev
# --no-dev: `uv run` re-syncs the environment before running, which would otherwise pull dev
# dependencies (pytest, moto, ...) back in despite the --no-dev sync above.
RUN cd picture_service && uv run --no-dev python scripts/gen_proto.py

FROM python:3.12-slim-bookworm AS runtime
WORKDIR /app

RUN useradd --system --create-home --home-dir /app appuser
USER appuser

COPY --from=build --chown=appuser:appuser /src/picture_service/.venv /app/.venv
COPY --from=build --chown=appuser:appuser /src/picture_service/src /app/src

ENV PATH="/app/.venv/bin:${PATH}" PYTHONPATH="/app/src"

EXPOSE 8080

# Required at runtime: PHOTOS_BUCKET_NAME, SIDECAR_TABLE_NAME, GEMINI_API_KEY (Fly secrets),
# CATALOG_SERVICE_ADDRESS, AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY (Fly secrets), AWS_REGION,
# OTEL_EXPORTER_OTLP_ENDPOINT/OTEL_EXPORTER_OTLP_HEADERS (Grafana Cloud - see main.py).
ENTRYPOINT ["python", "-m", "picture_service.main"]
