import httpx
import pytest


@pytest.fixture(autouse=True)
def prohibit_external_http(monkeypatch):
    def blocked(*_args, **_kwargs):
        raise AssertionError("External HTTP is disabled in tests. Use MockTransport.")

    async def blocked_async(*_args, **_kwargs):
        raise AssertionError("External HTTP is disabled in tests. Use MockTransport.")

    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", blocked)
    monkeypatch.setattr(httpx.AsyncHTTPTransport, "handle_async_request", blocked_async)
