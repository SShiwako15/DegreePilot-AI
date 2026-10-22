from dataclasses import replace

import httpx
import pytest
from fastapi.testclient import TestClient

from degreepilot_api.main import create_app
from degreepilot_api.settings import Settings

PASSWORD = "Local-Demo-Only!2026"


class FakeGateway:
    available = False
    reply = None
    before_reply = None

    async def status(self):
        return {"available": self.available, "reason": "Mocked provider status."}

    async def call(self, path, body=None, **kwargs):
        if self.before_reply:
            self.before_reply()
        if path == "advisor/answer" and self.available:
            from degreepilot_api.advisor import DISCLAIMER, LIMITED

            return self.reply or dict(
                status="ANSWERED", **body["drafts"][0], limitations=[DISCLAIMER, LIMITED]
            )
        return self.reply


@pytest.fixture(autouse=True)
def no_provider_http(monkeypatch):
    def blocked(*args, **kwargs):
        raise AssertionError("External HTTP is disabled in backend tests.")

    async def blocked_async(*args, **kwargs):
        raise AssertionError("External HTTP is disabled in backend tests.")

    monkeypatch.setattr(httpx.HTTPTransport, "handle_request", blocked)
    monkeypatch.setattr(httpx.AsyncHTTPTransport, "handle_async_request", blocked_async)


@pytest.fixture
def settings(tmp_path):
    return replace(
        Settings(),
        environment="Testing",
        demo_enabled=True,
        demo_password=PASSWORD,
        database=f"sqlite:///{(tmp_path / 'academic.db').as_posix()}",
        session_secret="test-only-" * 6,
        login_limit=1000,
        planner_limit=1000,
        advisor_limit=1000,
    )


@pytest.fixture
def app(settings):
    return create_app(settings, gateway=FakeGateway())


@pytest.fixture
def client(app):
    with TestClient(app, raise_server_exceptions=False) as connection:
        yield connection


def post(client, path, body=None, **kwargs):
    token = client.get("/api/auth/csrf").json()["token"]
    return client.post(path, json=body, headers={"X-CSRF-TOKEN": token}, **kwargs)


def login(client, username="student-ava"):
    response = post(client, "/api/auth/login", dict(username=username, password=PASSWORD))
    assert response.status_code == 204, response.text
