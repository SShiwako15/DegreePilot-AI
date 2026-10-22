from uuid import uuid4

from fastapi import HTTPException
from sqlalchemy import delete, select

from . import models as m
from .database import add_audit, course_model, course_row
from .domain import catalog_issues
from .policies import document_fingerprint, rules_fingerprint
from .schemas import Program


def require(condition, status, message):
    if not condition:
        raise HTTPException(status, message)


def editable(row, revision):
    require(row is not None, 404, "Document not found.")
    require(
        row.status == "draft" and row.revision == revision,
        409,
        "This draft changed or is already published. Refresh before retrying.",
    )


def assign(db, actor, request):
    advisor = db.get(m.User, request.advisor_id)
    require(
        advisor and advisor.role == "advisor" and db.get(m.Student, request.student_id),
        400,
        "Choose an existing advisor and student.",
    )
    row = db.get(m.Assignment, (request.advisor_id, request.student_id))
    require(
        (row.revision if row else 0) == request.revision, 409, "Assignment changed. Refresh before retrying."
    )
    if row:
        row.revision += 1
        row.active = request.active
    else:
        row = m.Assignment(
            advisor_id=request.advisor_id, student_id=request.student_id, active=request.active, revision=1
        )
        db.add(row)
    add_audit(
        db,
        actor,
        "assignment.grant" if request.active else "assignment.revoke",
        f"{request.advisor_id}/{request.student_id}",
        row.revision,
    )
    db.commit()


def clone_catalog(db, actor, request):
    require(not db.get(m.Catalog, request.catalog_version), 409, "That catalog version already exists.")
    source = db.get(m.Catalog, request.source_version)
    require(source and source.status == "published", 404, "Published source catalog not found.")
    program = Program.model_validate_json(source.rules_json).model_copy(
        update={"catalog_version": request.catalog_version}
    )
    courses = [
        course_model(c).model_copy(update={"catalog_version": request.catalog_version})
        for c in db.scalars(select(m.Course).where(m.Course.catalog_version == source.id))
    ]
    require(not catalog_issues(program, courses, program.catalog_version), 422, "Source catalog is invalid.")
    db.add(
        m.Catalog(
            id=program.catalog_version,
            program_id=program.program_id,
            rules_json=program.model_dump_json(),
            status="draft",
        )
    )
    db.flush()
    db.add_all(course_row(c) for c in courses)
    add_audit(db, actor, "catalog.draft", program.catalog_version)
    db.commit()
    return {"id": program.catalog_version}


def edit_catalog(db, actor, version, request):
    require(
        not catalog_issues(request.program, request.courses, version)
        and all(
            len(c.title) <= 200
            and len(c.prerequisite_groups) <= 30
            and all(len(g) <= 30 for g in c.prerequisite_groups)
            for c in request.courses
        ),
        422,
        "Catalog has invalid requirements, courses or prerequisite cycles.",
    )
    row = db.get(m.Catalog, version)
    editable(row, request.revision)
    db.execute(delete(m.Course).where(m.Course.catalog_version == version))
    db.flush()
    row.rules_json, row.program_id = request.program.model_dump_json(), request.program.program_id
    row.revision += 1
    db.add_all(course_row(c) for c in request.courses)
    add_audit(db, actor, "catalog.edit", version, row.revision)
    db.commit()


def publish_catalog(db, actor, version, revision):
    row = db.get(m.Catalog, version)
    editable(row, revision)
    program = Program.model_validate_json(row.rules_json)
    courses = [
        course_model(c) for c in db.scalars(select(m.Course).where(m.Course.catalog_version == version))
    ]
    require(not catalog_issues(program, courses, version), 422, "Catalog validation failed.")
    row.approval_hash = row.source_hash = rules_fingerprint(program, courses)
    row.status, row.revision = "published", row.revision + 1
    add_audit(db, actor, "catalog.publish", version, row.revision, row.approval_hash)
    db.commit()


def clone_policy(db, actor, request):
    source = db.get(m.PolicyDocument, request.source_document_id)
    require(source and source.status == "approved", 404, "Approved source document not found.")
    require(
        not db.scalar(
            select(m.PolicyDocument.id).where(
                m.PolicyDocument.replaces_document_id == source.id, m.PolicyDocument.status == "draft"
            )
        ),
        409,
        "A revision draft already exists for this document.",
    )
    draft = m.PolicyDocument(
        id=uuid4().hex,
        catalog_version=source.catalog_version,
        title=source.title,
        version=source.version + 1,
        replaces_document_id=source.id,
    )
    db.add(draft)
    db.flush()
    db.add_all(
        m.PolicySection(document_id=draft.id, id=s.id, title=s.title, text=s.text)
        for s in db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == source.id))
    )
    add_audit(db, actor, "policy.draft", draft.id)
    db.commit()
    return {"id": draft.id}


def edit_policy(db, actor, doc_id, request):
    require(
        request.title.strip()
        and len({s.id for s in request.sections}) == len(request.sections)
        and all(s.title.strip() and s.text.strip() for s in request.sections),
        422,
        "Supply unique source IDs, titles and policy passages.",
    )
    row = db.get(m.PolicyDocument, doc_id)
    editable(row, request.revision)
    db.execute(delete(m.PolicySection).where(m.PolicySection.document_id == doc_id))
    db.flush()
    row.title, row.revision = request.title, row.revision + 1
    db.add_all(m.PolicySection(document_id=doc_id, **s.model_dump()) for s in request.sections)
    add_audit(db, actor, "policy.edit", doc_id, row.revision)
    db.commit()


def publish_policy(db, actor, doc_id, revision):
    row = db.get(m.PolicyDocument, doc_id)
    editable(row, revision)
    source = db.get(m.PolicyDocument, row.replaces_document_id) if row.replaces_document_id else None
    require(
        source and source.status == "approved", 409, "The source was already retired. Create a new revision."
    )
    catalog = db.get(m.Catalog, row.catalog_version)
    require(catalog and catalog.status == "published", 422, "Publish the catalog first.")
    sections = list(db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == doc_id)))
    other_ids = set(
        db.scalars(
            select(m.PolicySection.id)
            .join(m.PolicyDocument)
            .where(
                m.PolicyDocument.catalog_version == row.catalog_version,
                m.PolicyDocument.status == "approved",
                m.PolicyDocument.id != source.id,
            )
        )
    )
    require(
        sections and all(s.id not in other_ids for s in sections),
        422,
        "Approved source IDs must be unambiguous in this catalog.",
    )
    row.approval_hash = row.source_hash = document_fingerprint(row, sections)
    row.status, row.revision = "approved", row.revision + 1
    source.status, source.revision = "retired", source.revision + 1
    add_audit(db, actor, "policy.publish", row.id, row.revision, row.approval_hash)
    add_audit(db, actor, "policy.retire", source.id, source.revision, source.source_hash)
    db.commit()


def seats(db, actor, request):
    require(request.available_seats <= request.capacity, 422, "Seat counts must be within capacity.")
    row = db.get(m.Section, (request.catalog_version, request.section_id))
    require(row, 404, "Section not found.")
    require(
        row.capacity == request.expected_capacity and row.available_seats == request.expected_available_seats,
        409,
        "Section changed. Refresh before retrying.",
    )
    row.capacity, row.available_seats = request.capacity, request.available_seats
    add_audit(db, actor, "section.seats", f"{request.catalog_version}/{request.section_id}")
    db.commit()
