## 1. PhotoStore and SidecarTable hold an already-open client

- [x] 1.1 Change `PhotoStore.__init__` (`picture_service/src/picture_service/photo_store.py`) to accept an already-open S3 client instead of a `Session` + `endpoint_url`, store it directly, and remove `_client()`/the per-method `async with self._client() as s3:` wrapper so every method uses the stored client directly. Verify `uv run pytest tests/test_photo_store.py` still exercises the same behavior once its fixture is updated (task 3.1).
- [x] 1.2 Change `SidecarTable.__init__` (`picture_service/src/picture_service/sidecar_table.py`) to accept an already-open DynamoDB resource instead of a `Session` + `endpoint_url`, store it directly, and remove `_resource()`/the per-method `async with self._resource() as dynamodb:` wrapper so every method uses the stored resource directly. Verify `uv run pytest tests/test_sidecar_table.py` still exercises the same behavior once its fixture is updated (task 3.2).

## 2. Own client lifetime from `main.py`

- [x] 2.1 In `_serve` (`picture_service/src/picture_service/main.py`), use `contextlib.AsyncExitStack` to open the S3 client (`session.client("s3", region_name=resolve_aws_region())`) and the DynamoDB resource (`session.resource("dynamodb", region_name=resolve_aws_region())`) once, before constructing `PhotoStore`/`SidecarTable`, and pass the opened objects into their constructors.
- [ ] 2.2 Close the `AsyncExitStack` (and therefore both clients) during shutdown, after `await server.stop(grace=5)`, so client teardown happens after the gRPC server has stopped accepting new work. Verify by running `uv run python -m picture_service.main` locally, sending SIGINT, and confirming clean shutdown with no unclosed-session warnings in the log.

## 3. Update tests for the new constructor shape

- [x] 3.1 Update the `photo_store` fixture (`picture_service/tests/test_photo_store.py`) to open the S3 client itself (async fixture opening `aws_session.client("s3", endpoint_url=aws_endpoint_url)` and yielding it into `PhotoStore`, closing on teardown) instead of passing `aws_session` + `endpoint_url` to `PhotoStore` directly.
- [x] 3.2 Update the `sidecar_table` fixture (`picture_service/tests/test_sidecar_table.py`) the same way for the DynamoDB resource.
- [x] 3.3 Run `uv run pytest` (from `picture_service/`) and confirm the full suite passes with no behavior changes.

## 4. Verify the fix against the reported regression

- [ ] 4.1 With `OTEL_EXPORTER_OTLP_ENDPOINT` pointed at the local Aspire Dashboard (`local-observability/README.md`), capture a trace of a `ListCards`-heavy page load (`/collection` or `/gallery`) against a collection with many photos, before and after this change, and confirm the repeated per-photo client/connection-setup cost seen before is gone.
