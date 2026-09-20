from picture_service.config import (
    ScannerConfig,
    resolve_aws_region,
    resolve_photos_bucket_name,
    resolve_sidecar_table_name,
)

ENV_KEYS = (
    "GEMINI_API_KEY",
    "GEMINI_MODEL",
    "CATALOG_SERVICE_ADDRESS",
    "OVERWRITE_SIDECARS",
    "DELAY_BETWEEN_REQUESTS_MS",
    "MAX_ATTEMPTS",
    "HTTP_TIMEOUT_SECONDS",
    "PHOTOS_BUCKET_NAME",
    "SIDECAR_TABLE_NAME",
    "AWS_REGION",
    "AWS_DEFAULT_REGION",
)


def _clear_env(monkeypatch):
    for key in ENV_KEYS:
        monkeypatch.delenv(key, raising=False)


def test_load_for_scan_uses_defaults_when_nothing_else_set(monkeypatch):
    _clear_env(monkeypatch)

    config = ScannerConfig.load_for_scan()

    assert config.api_key == ""
    assert config.model == "gemini-3.1-flash-lite"
    assert config.catalog_service_address == "http://localhost:5073"
    assert config.overwrite_existing_sidecars is False
    assert config.delay_between_requests_ms == 0
    assert config.max_attempts == 3
    assert config.timeout_seconds == 90


def test_load_for_scan_uses_env_vars_when_no_override_given(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("GEMINI_API_KEY", "env-key")
    monkeypatch.setenv("GEMINI_MODEL", "env-model")
    monkeypatch.setenv("CATALOG_SERVICE_ADDRESS", "http://env:1234")
    monkeypatch.setenv("OVERWRITE_SIDECARS", "true")
    monkeypatch.setenv("DELAY_BETWEEN_REQUESTS_MS", "500")
    monkeypatch.setenv("MAX_ATTEMPTS", "5")
    monkeypatch.setenv("HTTP_TIMEOUT_SECONDS", "120")

    config = ScannerConfig.load_for_scan()

    assert config.api_key == "env-key"
    assert config.model == "env-model"
    assert config.catalog_service_address == "http://env:1234"
    assert config.overwrite_existing_sidecars is True
    assert config.delay_between_requests_ms == 500
    assert config.max_attempts == 5
    assert config.timeout_seconds == 120


def test_load_for_scan_explicit_override_wins_over_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("GEMINI_API_KEY", "env-key")
    monkeypatch.setenv("GEMINI_MODEL", "env-model")

    config = ScannerConfig.load_for_scan(api_key="override-key", model="override-model")

    assert config.api_key == "override-key"
    assert config.model == "override-model"


def test_load_for_scan_clamps_max_attempts_and_timeout(monkeypatch):
    _clear_env(monkeypatch)

    config = ScannerConfig.load_for_scan(max_attempts=0, timeout_seconds=1)

    assert config.max_attempts == 1
    assert config.timeout_seconds == 10


def test_load_for_upload_forces_overwrite_and_zero_delay(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("OVERWRITE_SIDECARS", "false")
    monkeypatch.setenv("DELAY_BETWEEN_REQUESTS_MS", "999")

    config = ScannerConfig.load_for_upload()

    assert config.overwrite_existing_sidecars is True
    assert config.delay_between_requests_ms == 0


def test_load_for_upload_override_wins_over_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("MAX_ATTEMPTS", "1")

    config = ScannerConfig.load_for_upload(max_attempts=42)

    assert config.max_attempts == 42


def test_load_for_upload_uses_default_when_neither_override_nor_env(monkeypatch):
    _clear_env(monkeypatch)

    config = ScannerConfig.load_for_upload()

    assert config.max_attempts == 3
    assert config.timeout_seconds == 90


def test_resolve_photos_bucket_name_from_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("PHOTOS_BUCKET_NAME", "my-bucket")

    assert resolve_photos_bucket_name() == "my-bucket"


def test_resolve_photos_bucket_name_missing_raises(monkeypatch):
    _clear_env(monkeypatch)

    try:
        resolve_photos_bucket_name()
        assert False, "expected RuntimeError"
    except RuntimeError:
        pass


def test_resolve_sidecar_table_name_from_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("SIDECAR_TABLE_NAME", "my-table")

    assert resolve_sidecar_table_name() == "my-table"


def test_resolve_sidecar_table_name_missing_raises(monkeypatch):
    _clear_env(monkeypatch)

    try:
        resolve_sidecar_table_name()
        assert False, "expected RuntimeError"
    except RuntimeError:
        pass


def test_resolve_aws_region_from_aws_region_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("AWS_REGION", "eu-central-1")

    assert resolve_aws_region() == "eu-central-1"


def test_resolve_aws_region_falls_back_to_aws_default_region_env(monkeypatch):
    _clear_env(monkeypatch)
    monkeypatch.setenv("AWS_DEFAULT_REGION", "eu-central-1")

    assert resolve_aws_region() == "eu-central-1"


def test_resolve_aws_region_missing_raises(monkeypatch):
    _clear_env(monkeypatch)

    try:
        resolve_aws_region()
        assert False, "expected RuntimeError"
    except RuntimeError:
        pass
