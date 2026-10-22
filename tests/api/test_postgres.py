"""Real migration checks use disposable databases, never the configured application database."""

import os
from dataclasses import replace
from pathlib import Path
from uuid import uuid4

import psycopg
import pytest
from conftest import login, post
from fastapi.testclient import TestClient
from psycopg import sql
from sqlalchemy import text
from sqlalchemy.engine import make_url

from degreepilot_api import models as m
from degreepilot_api.database import make_engine
from degreepilot_api.main import create_app
from degreepilot_api.security import passwords
from degreepilot_api.settings import database_url


def connect_database(**kwargs):
    try:
        return psycopg.connect(connect_timeout=5, **kwargs)
    except psycopg.Error:
        pytest.fail(
            "The disposable PostgreSQL connection failed. Check local service availability.", pytrace=False
        )


@pytest.mark.skipif(
    not os.getenv("DEGREEPILOT_TEST_DATABASE"),
    reason="Set DEGREEPILOT_TEST_DATABASE for disposable PostgreSQL checks.",
)
@pytest.mark.parametrize("legacy", [False, True])
def test_real_postgres_migration_seed_restart_and_legacy_preservation(settings, legacy):
    base = make_url(database_url(os.environ["DEGREEPILOT_TEST_DATABASE"]))
    name = "degreepilot_test_" + uuid4().hex
    connect = dict(host=base.host, port=base.port or 5432, user=base.username, password=base.password)
    with connect_database(dbname=base.database, autocommit=True, **connect) as admin:
        admin.execute(sql.SQL("CREATE DATABASE {} TEMPLATE template0").format(sql.Identifier(name)))
    try:
        url = base.set(database=name).render_as_string(hide_password=False)
        if legacy:
            script = (Path(__file__).parent / "fixtures/legacy_schema.sql").read_text(encoding="utf-8-sig")
            with connect_database(dbname=name, autocommit=True, **connect) as connection:
                connection.execute(script, prepare=False)
        configured = replace(settings, database=url)
        app = create_app(configured)
        with TestClient(app) as client:
            assert client.get("/health/ready").status_code == 200
            login(client)
            assert client.get("/api/students/me/degree-audit").json()["earnedCredits"] == 35
            login(client, "student-noah")
            assert client.get("/api/students/student-ava/transcript").status_code == 404
            login(client, "admin-robin")
            assert (
                post(
                    client,
                    "/api/admin/assignments",
                    dict(advisorId="advisor-morgan", studentId="student-ava", active=False, revision=1),
                ).status_code
                == 204
            )
            with app.state.sessions.begin() as db:
                db.get(m.User, "student-ava").password_hash = passwords.hash("Retained-Local-Password!")
            with app.state.engine.connect() as connection:
                before = connection.execute(text('SELECT * FROM "Attempts" ORDER BY "Id"')).all()
        restarted = create_app(configured)
        with TestClient(restarted) as client:
            with restarted.state.engine.connect() as connection:
                assert connection.execute(text('SELECT * FROM "Attempts" ORDER BY "Id"')).all() == before
            assert (
                post(
                    client,
                    "/api/auth/login",
                    dict(username="student-ava", password="Retained-Local-Password!"),
                ).status_code
                == 204
            )
            login(client, "advisor-morgan")
            assert client.get("/api/students/student-ava/transcript").status_code == 404
        if legacy:
            # Adopt a populated legacy schema too; changing only the migration marker must preserve every record.
            engine = make_engine(url)
            with engine.begin() as connection:
                connection.execute(text("DROP TABLE alembic_version"))
            engine.dispose()
            adopted = create_app(configured)
            with TestClient(adopted):
                with adopted.state.engine.connect() as connection:
                    assert connection.execute(text('SELECT * FROM "Attempts" ORDER BY "Id"')).all() == before
    finally:
        with connect_database(dbname=base.database, autocommit=True, **connect) as admin:
            admin.execute(sql.SQL("DROP DATABASE {} WITH (FORCE)").format(sql.Identifier(name)))
