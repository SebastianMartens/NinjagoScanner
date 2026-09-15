"""Shared test fixtures. Mocked AWS tests use a real ThreadedMotoServer instance rather than
moto's `mock_aws` HTTP-patching decorator, because that decorator patches at the urllib3 layer
and is incompatible with aiobotocore's aiohttp-based transport (aiobotocore awaits response
bodies moto's patch returns as plain bytes). Pointing aioboto3 at a real (mocked) HTTP server
via endpoint_url sidesteps that entirely.
"""

from __future__ import annotations

from collections.abc import AsyncIterator

import aioboto3
import pytest
import pytest_asyncio
import requests
from moto.moto_server.threaded_moto_server import ThreadedMotoServer


@pytest.fixture(scope="session")
def moto_server() -> AsyncIterator[str]:
    server = ThreadedMotoServer(ip_address="127.0.0.1", port=0)
    server.start()
    _, port = server.get_host_and_port()
    yield f"http://127.0.0.1:{port}"
    server.stop()


@pytest.fixture(autouse=True)
def _reset_moto_state(moto_server: str):
    # The server fixture is session-scoped (starting/stopping it per test is slow), so every
    # test resets its backend state up front instead, keeping tests isolated (e.g. DynamoDB
    # CreateTable failing on a name a previous test already created).
    requests.post(f"{moto_server}/moto-api/reset")


@pytest_asyncio.fixture
async def aws_session(moto_server: str) -> aioboto3.Session:
    return aioboto3.Session(
        region_name="us-east-1",
        aws_access_key_id="testing",
        aws_secret_access_key="testing",
    )


@pytest.fixture
def aws_endpoint_url(moto_server: str) -> str:
    return moto_server
