import json
from uuid import uuid4

from sqlalchemy import select

from . import models as m
from .database import academic, course_row
from .domain import audit, catalog_issues, valid_section, valid_term
from .schemas import Course, Program, Section, Term
from .security import passwords
from .settings import ROOT


def fixture(name: str):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8-sig"))


def seed(db, password: str, sources):
    if len(password) < 12:
        raise RuntimeError("Demo password must contain at least 12 characters.")
    program = Program.model_validate(fixture("program.json"))
    courses = [Course.model_validate(c) for c in fixture("courses.json")]
    if catalog_issues(program, courses, program.catalog_version):
        raise RuntimeError("CATALOG_INVALID: synthetic catalog validation failed.")
    edition = db.get(m.Catalog, program.catalog_version)
    if edition is None:
        db.add(
            m.Catalog(
                id=program.catalog_version,
                program_id=program.program_id,
                rules_json=program.model_dump_json(),
                source_hash=sources.rules_fingerprint,
            )
        )
        db.flush()
        db.add_all(course_row(c) for c in courses)
        db.flush()
    else:
        # Compare semantic rules to avoid changing pinned fingerprints when JSON serializers differ.
        stored = Program.model_validate_json(edition.rules_json)
        from .database import course_model

        existing = [
            course_model(c)
            for c in db.scalars(select(m.Course).where(m.Course.catalog_version == program.catalog_version))
        ]
        if stored != program or sorted(existing, key=lambda c: c.code) != sorted(
            courses, key=lambda c: c.code
        ):
            raise RuntimeError("Pinned catalog fixtures changed. Publish a new catalog version.")
    users = fixture("students.json")
    for entry in users:
        if db.get(m.User, entry["id"]):
            continue
        db.add(
            m.User(
                id=entry["id"],
                display_name=entry["display_name"],
                email=entry["email"],
                role=entry["role"],
                password_hash=passwords.hash(password),
            )
        )
        db.flush()
        if entry["role"] == "student":
            if (
                entry["catalog_version"] != program.catalog_version
                or entry["program_id"] != program.program_id
            ):
                raise RuntimeError("Invalid synthetic student catalog.")
            db.add(m.Student(user_id=entry["id"], catalog_version=program.catalog_version))
            db.flush()
            for attempt in entry["transcript"]:
                db.add(
                    m.Attempt(
                        id=uuid4(),
                        student_id=entry["id"],
                        catalog_version=program.catalog_version,
                        course_code=attempt["course_code"],
                        term=attempt["term"],
                        grade=attempt["grade"],
                    )
                )
            db.flush()
            data = academic(db, entry["id"])
            if audit(data[1], data[2], data[3], data[0])["status"] != "valid":
                raise RuntimeError("TRANSCRIPT_INVALID: synthetic student validation failed.")
    for entry in users:
        for student_id in entry.get("assigned_student_ids", []):
            if not db.get(m.Assignment, (entry["id"], student_id)):
                db.add(m.Assignment(advisor_id=entry["id"], student_id=student_id, active=True))
    terms = [Term.model_validate(t) for t in fixture("terms.json")]
    if len({t.code for t in terms}) != len(terms) or not all(valid_term(t) for t in terms):
        raise RuntimeError("Invalid term fixtures.")
    for term in terms:
        if not db.get(m.Term, term.code):
            db.add(m.Term(**term.model_dump()))
    db.flush()
    for entry in fixture("sections.json"):
        section = Section(catalog_version=program.catalog_version, **entry)
        term = next((t for t in terms if t.code == section.term_code), None)
        if not term or not term.is_published or not valid_section(section, term):
            raise RuntimeError("Invalid section fixtures.")
        if not db.get(m.Section, (program.catalog_version, section.id)):
            data = section.model_dump(exclude={"days"})
            db.add(m.Section(**data, days_json=json.dumps(section.days)))
    db.flush()
    sources.seed(db)
