from conftest import login, post
from sqlalchemy import select

from degreepilot_api import models as m


def test_assignment_revision_audit_and_revocation(client):
    login(client, "admin-robin")
    body = dict(advisorId="advisor-morgan", studentId="student-ava", active=False, revision=1)
    assert post(client, "/api/admin/assignments", body).status_code == 204
    assert post(client, "/api/admin/assignments", body).status_code == 409
    assert "assignment.revoke" in [r["action"] for r in client.get("/api/admin/audit").json()]
    directory = client.get("/api/admin/directory").json()
    assert not directory["assignments"][0]["active"]
    assert "passwordHash" not in str(directory) and "email" not in str(directory)


def test_catalog_draft_validation_publishing_and_pins(client, app):
    login(client, "admin-robin")
    version = "NSU-BSCS-2027"
    assert (
        post(
            client, "/api/admin/catalogs", dict(sourceVersion="NSU-BSCS-2026", catalogVersion=version)
        ).status_code
        == 200
    )
    draft = client.get(f"/api/admin/catalogs/{version}").json()
    draft["program"]["minTotalCredits"] = 119
    body = {k: draft[k] for k in ("revision", "program", "courses")}
    assert post(client, f"/api/admin/catalogs/{version}/draft", body).status_code == 204
    assert post(client, f"/api/admin/catalogs/{version}/draft", body).status_code == 409
    assert post(client, f"/api/admin/catalogs/{version}/publish", {"revision": 2}).status_code == 204
    assert post(client, f"/api/admin/catalogs/{version}/publish", {"revision": 3}).status_code == 409
    with app.state.sessions() as db:
        assert db.get(m.Student, "student-ava").catalog_version == "NSU-BSCS-2026"
        assert db.get(m.Catalog, version).approval_hash


def test_policy_publication_preserves_history_and_rejects_stale_edits(client, app):
    login(client, "admin-robin")
    source = client.get("/api/admin/policies").json()[0]
    result = post(client, "/api/admin/policies", {"sourceDocumentId": source["id"]})
    assert result.status_code == 200
    doc_id = result.json()["id"]
    detail = client.get(f"/api/admin/policies/{doc_id}").json()
    body = dict(
        revision=detail["document"]["revision"],
        title=detail["document"]["title"],
        sections=detail["sections"],
    )
    body["sections"][0]["text"] += " Reviewed revision."
    assert post(client, f"/api/admin/policies/{doc_id}/draft", body).status_code == 204
    assert post(client, f"/api/admin/policies/{doc_id}/publish", {"revision": 2}).status_code == 204
    assert post(client, f"/api/admin/policies/{doc_id}/draft", body).status_code == 409
    with app.state.sessions() as db:
        assert db.get(m.PolicyDocument, source["id"]).status == "retired"
        assert db.get(m.PolicyDocument, doc_id).status == "approved"
        assert list(db.scalars(select(m.MutationAudit).where(m.MutationAudit.action == "policy.retire")))


def test_seat_bounds_and_optimistic_concurrency(client):
    login(client, "admin-robin")
    row = client.get("/api/admin/sections").json()[0]
    body = dict(
        catalogVersion=row["catalogVersion"],
        sectionId=row["id"],
        capacity=row["capacity"],
        availableSeats=0,
        expectedCapacity=row["capacity"],
        expectedAvailableSeats=row["availableSeats"],
    )
    assert post(client, "/api/admin/sections/seats", {**body, "availableSeats": 10000}).status_code == 422
    assert post(client, "/api/admin/sections/seats", body).status_code == 204
    assert post(client, "/api/admin/sections/seats", body).status_code == 409
