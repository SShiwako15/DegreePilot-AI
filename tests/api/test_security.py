from dataclasses import replace

import pytest
from conftest import login, post
from fastapi.testclient import TestClient

from degreepilot_api import models as m
from degreepilot_api.main import create_app


def test_anonymous_and_csrf_boundaries(client):
    assert client.get("/api/me").status_code == 401
    assert (
        client.post("/api/auth/login", json={"username": "student-ava", "password": "bad"}).status_code == 400
    )
    assert (
        post(client, "/api/auth/login", {"username": "student-ava", "password": "wrong"}).status_code == 401
    )
    login(client)
    assert client.get("/api/me").json()["role"] == "student"
    assert post(client, "/api/auth/logout").status_code == 204
    assert client.get("/api/me").status_code == 401


def test_tokens_are_session_bound(client, app):
    token = client.get("/api/auth/csrf").json()["token"]
    with TestClient(app) as other:
        other.get("/api/auth/csrf")
        assert (
            other.post(
                "/api/auth/login",
                json={"username": "student-ava", "password": "wrong"},
                headers={"X-CSRF-TOKEN": token},
            ).status_code
            == 400
        )


@pytest.mark.parametrize(
    "user,role", [("student-ava", "student"), ("advisor-morgan", "advisor"), ("admin-robin", "admin")]
)
def test_role_and_owner_authorization(client, user, role):
    login(client, user)
    assert client.get("/api/me").json()["role"] == role
    assert client.get("/api/admin/directory").status_code == (200 if role == "admin" else 403)
    assert client.get("/api/advisor-workspace/students").status_code == (200 if role == "advisor" else 403)
    assert client.get("/api/students/me/transcript").status_code == (200 if role == "student" else 403)
    assert client.get("/api/students/student-ava/transcript").status_code == (403 if role == "admin" else 200)
    assert client.get("/api/students/student-noah/transcript").status_code == (
        403 if role == "admin" else 404
    )
    if role == "student":
        assert (
            client.get("/api/students/me/degree-audit?studentId=student-noah").json()["earnedCredits"] == 35
        )


def test_student_cannot_submit_other_records(client):
    login(client)
    body = {"termCode": "2027-SPRING", "minCredits": 12, "maxCredits": 12, "studentId": "student-noah"}
    assert post(client, "/api/plans/generate", body).status_code == 400
    assert post(client, "/api/admin/assignments", {}).status_code == 403


def test_revoked_assignment_is_checked_each_request(client, app):
    login(client, "advisor-morgan")
    assert client.get("/api/students/student-ava/degree-audit").status_code == 200
    with app.state.sessions.begin() as db:
        row = db.get(m.Assignment, ("advisor-morgan", "student-ava"))
        row.active = False
        row.revision += 1
    assert client.get("/api/students/student-ava/degree-audit").status_code == 404
    assert client.get("/api/advisor-workspace/students").json() == []


def test_cookie_headers_origin_and_limits(client):
    token_response = client.get("/api/auth/csrf")
    cookie = token_response.headers["set-cookie"].lower()
    assert "httponly" in cookie and "samesite=strict" in cookie
    token = token_response.json()["token"]
    assert (
        client.post(
            "/api/auth/login", json={}, headers={"X-CSRF-TOKEN": token, "Origin": "https://other.invalid"}
        ).status_code
        == 400
    )
    oversized = client.post(
        "/api/auth/login", content=b"x" * (512 * 1024 + 1), headers={"X-CSRF-TOKEN": token}
    )
    assert oversized.status_code == 413
    for header in ("Cache-Control", "X-Content-Type-Options", "X-Frame-Options", "X-Request-ID"):
        assert header in token_response.headers
    assert "wrong" not in post(client, "/api/auth/login", {"password": "wrong"}).text


def test_rate_limiting(settings):
    with TestClient(create_app(replace(settings, login_limit=1))) as client:
        assert (
            post(client, "/api/auth/login", {"username": "missing", "password": "wrong"}).status_code == 401
        )
        response = post(client, "/api/auth/login", {"username": "missing", "password": "wrong"})
        assert response.status_code == 429 and response.headers["retry-after"] == "60"


def test_demo_refused_outside_local(settings):
    with pytest.raises(RuntimeError):
        create_app(replace(settings, environment="Production"))


def test_session_role_is_rechecked(client, app):
    login(client)
    with app.state.sessions.begin() as db:
        db.get(m.User, "student-ava").role = "admin"
    assert client.get("/api/students/me/transcript").status_code == 403


def test_safe_service_error(client, app):
    login(client)

    def broken():
        raise RuntimeError("transcript-secret-do-not-publish")

    app.state.gateway.status = broken
    response = client.get("/api/me")
    assert response.status_code == 500 and "transcript-secret" not in response.text
