import json
from pathlib import Path

import pytest
from conftest import PASSWORD, login, post
from fastapi.testclient import TestClient
from sqlalchemy import select, text

from degreepilot_api import models as m
from degreepilot_api.database import make_engine
from degreepilot_api.main import create_app
from degreepilot_api.migrate import LEGACY_REVISIONS, migrate
from degreepilot_api.policies import Sources, document_fingerprint
from degreepilot_api.security import passwords, verify_password

REFERENCE = json.loads((Path(__file__).parent / "fixtures/legacy_contract.json").read_text(encoding="utf-8"))


def test_password_compatibility_and_upgrade(client, app):
    encoded = REFERENCE["passwordHash"]
    assert verify_password(PASSWORD, encoded) == (True, True)
    assert verify_password("wrong-password", encoded) == (False, False)
    with app.state.sessions.begin() as db:
        db.get(m.User, "student-ava").password_hash = encoded
    login(client)
    with app.state.sessions() as db:
        assert db.get(m.User, "student-ava").password_hash.startswith("$argon2")
    assert verify_password(PASSWORD, passwords.hash(PASSWORD)) == (True, False)
    for malformed in ("", "bad", "AQ==", "AAAA"):
        assert not verify_password(PASSWORD, malformed)[0]


def test_reference_approval_hashes(client, app):
    assert Sources().rules_fingerprint == REFERENCE["rulesFingerprint"]
    with app.state.sessions() as db:
        for expected in REFERENCE["documents"]:
            doc = db.get(m.PolicyDocument, expected["id"])
            sections = list(db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == doc.id)))
            assert document_fingerprint(doc, sections) == expected["hash"]


def test_reference_audits_and_all_eligibility_results(client):
    for expected in REFERENCE["students"]:
        login(client, expected["id"])
        result = client.get("/api/students/me/degree-audit").json()
        for key in ("generatedAt", "ruleEngineVersion"):
            result.pop(key)
        expected_audit = {
            k: v for k, v in expected["audit"].items() if k not in {"generatedAt", "ruleEngineVersion"}
        }
        assert result == expected_audit
        for term in expected["terms"]:
            rows = client.get("/api/courses", params={"term": term["code"]}).json()
            assert {row["course"]["code"]: row["eligibility"] for row in rows} == {
                row["courseCode"]: row for row in term["eligibility"]
            }
        post(client, "/api/auth/logout")


def test_restart_preserves_mutations_and_does_not_reset_password(settings):
    first = create_app(settings)
    with TestClient(first) as client:
        login(client)
        with first.state.sessions.begin() as db:
            row = db.get(m.Assignment, ("advisor-morgan", "student-ava"))
            row.active, row.revision = False, 2
            db.get(m.User, "student-ava").password_hash = passwords.hash("Another-Local-Test-Password!")
    second = create_app(settings)
    with TestClient(second):
        with second.state.sessions() as db:
            assert not db.get(m.Assignment, ("advisor-morgan", "student-ava")).active
            assert verify_password(
                "Another-Local-Test-Password!", db.get(m.User, "student-ava").password_hash
            )[0]
            assert len(list(db.scalars(select(m.Attempt)))) == 13


def test_append_only_audit(client, app):
    login(client, "admin-robin")
    post(
        client,
        "/api/admin/assignments",
        dict(advisorId="advisor-morgan", studentId="student-ava", active=False, revision=1),
    )
    with app.state.sessions() as db:
        row = db.scalar(select(m.MutationAudit))
        row.action = "forged"
        with pytest.raises(ValueError, match="append-only"):
            db.flush()
        db.rollback()


def test_partial_or_unrecognized_schema_is_rejected(tmp_path):
    engine = make_engine(f"sqlite:///{(tmp_path / 'partial.db').as_posix()}")
    with engine.begin() as connection:
        connection.execute(text('CREATE TABLE "Users" ("Id" TEXT PRIMARY KEY)'))
    with pytest.raises(RuntimeError, match="migration history"):
        migrate(engine)


def test_legacy_adoption_does_not_rewrite_data(settings):
    first = create_app(settings)
    with TestClient(first):
        with first.state.engine.begin() as connection:
            connection.execute(text("DROP TABLE alembic_version"))
            connection.execute(
                text(
                    'CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT)'
                )
            )
            for revision in LEGACY_REVISIONS:
                connection.execute(
                    text("INSERT INTO \"__EFMigrationsHistory\" VALUES (:revision, '10.0.4')"),
                    dict(revision=revision),
                )
            before = connection.execute(text('SELECT * FROM "Attempts" ORDER BY "Id"')).all()
    second = create_app(settings)
    with TestClient(second):
        with second.state.engine.connect() as connection:
            assert connection.execute(text('SELECT * FROM "Attempts" ORDER BY "Id"')).all() == before
            assert connection.scalar(text("SELECT version_num FROM alembic_version")) == "0001"
