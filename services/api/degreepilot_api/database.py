import json
from datetime import UTC, datetime
from uuid import uuid4

from sqlalchemy import create_engine, event, select
from sqlalchemy.orm import Session, sessionmaker

from . import models as m
from .domain import Context, audit
from .schemas import Attempt, Course, Program, Section, Term


def make_engine(url: str):
    engine = create_engine(
        url,
        echo=False,
        hide_parameters=True,
        **(
            {"connect_args": {"check_same_thread": False}}
            if url.startswith("sqlite")
            else {
                "isolation_level": "SERIALIZABLE",
                "pool_pre_ping": True,
                "connect_args": {"connect_timeout": 5},
            }
        ),
    )
    if url.startswith("sqlite"):

        @event.listens_for(engine, "connect")
        def foreign_keys(connection, _record):
            connection.execute("PRAGMA foreign_keys=ON")

    return engine


def sessions(engine):
    return sessionmaker(engine, expire_on_commit=False)


def public(model) -> dict:
    return {
        column.key[0].lower() + column.key[1:]: getattr(model, attr.key)
        for attr in model.__mapper__.column_attrs
        for column in attr.columns
    }


def course_model(row: m.Course) -> Course:
    return Course(
        code=row.code,
        title=row.title,
        credits=row.credits,
        category=row.category,
        prerequisite_groups=json.loads(row.prerequisites_json),
        offered_terms=json.loads(row.offered_terms_json),
        catalog_version=row.catalog_version,
    )


def course_row(course: Course) -> m.Course:
    return m.Course(
        catalog_version=course.catalog_version,
        code=course.code,
        title=course.title,
        credits=course.credits,
        category=course.category,
        prerequisites_json=json.dumps(course.prerequisite_groups),
        offered_terms_json=json.dumps(course.offered_terms),
    )


def term_model(row: m.Term) -> Term:
    return Term.model_validate(public(row))


def section_model(row: m.Section) -> Section:
    data = public(row)
    data["days"] = json.loads(data.pop("daysJson"))
    return Section.model_validate(data)


def academic(db: Session, student_id: str):
    student = db.get(m.Student, student_id)
    if student is None:
        return None
    edition = db.get(m.Catalog, student.catalog_version)
    try:
        program = Program.model_validate_json(edition.rules_json) if edition else None
        courses = [
            course_model(c)
            for c in db.scalars(
                select(m.Course)
                .where(m.Course.catalog_version == student.catalog_version)
                .order_by(m.Course.code)
            )
        ]
        attempts = [
            Attempt(course_code=a.course_code, term=a.term, grade=a.grade)
            for a in db.scalars(select(m.Attempt).where(m.Attempt.student_id == student_id))
        ]
        return student.catalog_version, program, courses, attempts
    except ValueError:
        return student.catalog_version, None, [], []


def degree_audit(db: Session, student_id: str):
    data = academic(db, student_id)
    return audit(data[1], data[2], data[3], data[0]) if data else None


def load_context(db: Session, student_id: str, term_code: str) -> Context | None:
    data = academic(db, student_id)
    term = db.get(m.Term, term_code)
    if data is None or term is None:
        return None
    try:
        sections = [
            section_model(s)
            for s in db.scalars(
                select(m.Section)
                .where(m.Section.catalog_version == data[0], m.Section.term_code == term_code)
                .order_by(m.Section.id)
            )
        ]
        return Context(data[0], data[1], data[2], data[3], term_model(term), sections)
    except ValueError:
        return Context(data[0], None, [], [], term_model(term), [])


def add_audit(db: Session, actor: str, action: str, resource: str, revision: int = 0, content_hash: str = ""):
    db.add(
        m.MutationAudit(
            id=uuid4(),
            actor_id=actor,
            action=action,
            resource_id=resource,
            revision=revision,
            content_hash=content_hash,
            occurred_at=datetime.now(UTC),
        )
    )
