"""A minimal fake grpc.aio.ServicerContext for unit-testing servicer methods without a real
gRPC server/channel. abort() raises AbortCalled, mirroring real grpc.aio's abort() always
raising to terminate the RPC.
"""

from __future__ import annotations

import grpc


class AbortCalled(Exception):
    def __init__(self, code: grpc.StatusCode, details: str) -> None:
        self.code = code
        self.details = details
        super().__init__(f"{code}: {details}")


class FakeServicerContext:
    async def abort(self, code: grpc.StatusCode, details: str = "") -> None:
        raise AbortCalled(code, details)
