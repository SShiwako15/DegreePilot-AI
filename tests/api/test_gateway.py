import asyncio

import httpx
import pytest

from degreepilot_api.gateway import Gateway


@pytest.mark.parametrize("status,body", [(503, b"{}"), (200, b"not-json"), (200, b"[]"), (302, b"{}")])
def test_internal_client_rejects_untrusted_http_responses(status, body):
    transport = httpx.MockTransport(lambda _request: httpx.Response(status, content=body))
    gateway = Gateway("http://internal/", transport=transport)
    assert asyncio.run(gateway.call("solve", {"options": []})) is None


def test_internal_client_limits_buffered_response_size():
    transport = httpx.MockTransport(lambda _request: httpx.Response(200, content=b"x" * 2048))
    gateway = Gateway("http://internal/", transport=transport)
    assert asyncio.run(gateway.call("solve", {}, limit=1024)) is None


def test_internal_client_timeout_has_safe_fallback():
    def timeout(_request):
        raise httpx.ReadTimeout("private-service-diagnostic")

    gateway = Gateway("http://internal/", transport=httpx.MockTransport(timeout))
    assert asyncio.run(gateway.call("solve", {})) is None
    status = asyncio.run(gateway.status())
    assert not status["available"] and "private-service-diagnostic" not in str(status)


def test_internal_client_uses_private_path_and_validates_availability():
    seen = []

    def respond(request):
        seen.append(str(request.url))
        return httpx.Response(200, json={"available": "yes", "reason": "bad status"})

    gateway = Gateway("http://internal/", transport=httpx.MockTransport(respond))
    assert not asyncio.run(gateway.status())["available"]
    assert seen == ["http://internal/advisor/status"]
