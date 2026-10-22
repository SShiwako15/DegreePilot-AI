import hashlib
import json
import re
from collections import defaultdict

from sqlalchemy import select

from . import models as m
from .database import course_model
from .domain import catalog_issues
from .schemas import Course, Program
from .settings import ROOT


def canonical(value) -> str:
    """Preserve historical approval hashes written by the previous JSON serializer."""
    text = json.dumps(value, ensure_ascii=True, separators=(",", ":"))
    for char in ("'", "&", "+", "<", ">", "`"):
        text = text.replace(char, f"\\u{ord(char):04X}")
    text = text.replace('\\"', r"\u0022")
    return re.sub(r"\\u([0-9a-fA-F]{4})", lambda match: r"\u" + match[1].upper(), text)


def digest(text: str) -> str:
    return hashlib.sha256(text.encode()).hexdigest().upper()


def rules_fingerprint(program: Program, courses: list[Course]) -> str:
    return digest(
        canonical(program.model_dump())
        + canonical([c.model_dump() for c in sorted(courses, key=lambda c: c.code)])
    )


def document_fingerprint(doc: m.PolicyDocument, sections: list[m.PolicySection]) -> str:
    return digest(
        canonical(
            dict(
                Id=doc.id,
                CatalogVersion=doc.catalog_version,
                Title=doc.title,
                Version=doc.version,
                sections=[
                    dict(Id=s.id, Title=s.title, Text=s.text) for s in sorted(sections, key=lambda s: s.id)
                ],
            )
        )
    )


class Sources:
    def __init__(self):
        from .seed import fixture

        self.program = Program.model_validate(fixture("program.json"))
        self.courses = [Course.model_validate(c) for c in fixture("courses.json")]
        self.rules_fingerprint = rules_fingerprint(self.program, self.courses)
        self.sections = []
        for approval in json.loads((ROOT / "policies/manifest.json").read_text(encoding="utf-8-sig")):
            if approval["status"] != "approved":
                continue
            name = approval["file"]
            if "/" in name or "\\" in name or name in {".", ".."} or approval["version"] < 1:
                raise RuntimeError("Invalid policy approval.")
            text = (ROOT / "policies" / name).read_text(encoding="utf-8-sig").replace("\r\n", "\n")
            if digest(text).lower() != approval["sha256"].lower():
                raise RuntimeError("Policy content changed. Review and version its approval.")
            headings = list(re.finditer(r"^## ([A-Z][A-Z0-9-]+) — (.+)$", text, re.M))
            if not headings:
                raise RuntimeError("Approved policy has no identifiable sections.")
            for index, heading in enumerate(headings):
                body = text[
                    heading.end() : headings[index + 1].start() if index + 1 < len(headings) else len(text)
                ].strip()
                if not 1 <= len(body) <= 3000:
                    raise RuntimeError("Invalid policy section size.")
                self.sections.append(
                    dict(
                        documentId=f"{approval['catalogVersion']}:{name.rsplit('.', 1)[0]}:{approval['version']}",
                        catalogVersion=approval["catalogVersion"],
                        documentTitle=text.splitlines()[0].lstrip("# "),
                        documentVersion=approval["version"],
                        sourceHash=approval["sha256"],
                        policySectionId=heading[1],
                        title=heading[2].strip(),
                        quote=body,
                    )
                )
        if len({(s["catalogVersion"], s["policySectionId"]) for s in self.sections}) != len(self.sections):
            raise RuntimeError("Ambiguous approved source IDs.")

    def matches(self, doc, section):
        return any(
            s["documentId"] == doc.id
            and s["catalogVersion"] == doc.catalog_version
            and s["documentTitle"] == doc.title
            and s["documentVersion"] == doc.version
            and s["sourceHash"] == doc.source_hash
            and s["policySectionId"] == section.id
            and s["title"] == section.title
            and s["quote"] == section.text
            for s in self.sections
        )

    def seed(self, db):
        groups = defaultdict(list)
        for section in self.sections:
            groups[section["documentId"]].append(section)
        for doc_id, approved in groups.items():
            first = approved[0]
            doc = db.get(m.PolicyDocument, doc_id)
            if doc is None or doc.status == "approved":
                for prior in db.scalars(
                    select(m.PolicyDocument).where(
                        m.PolicyDocument.catalog_version == first["catalogVersion"],
                        m.PolicyDocument.status == "approved",
                        m.PolicyDocument.id != doc_id,
                    )
                ):
                    passages = list(
                        db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == prior.id))
                    )
                    if not passages or not all(
                        any(
                            s["policySectionId"] == p.id and s["title"] == p.title and s["quote"] == p.text
                            for s in approved
                        )
                        for p in passages
                    ):
                        continue
                    if prior.approval_hash and prior.approval_hash != document_fingerprint(prior, passages):
                        continue
                    prior.status, prior.revision = "retired", prior.revision + 1
            if doc is None:
                doc = m.PolicyDocument(
                    id=doc_id,
                    catalog_version=first["catalogVersion"],
                    title=first["documentTitle"],
                    version=first["documentVersion"],
                    status="approved",
                    source_hash=first["sourceHash"],
                )
                db.add(doc)
                db.flush()
                db.add_all(
                    m.PolicySection(
                        document_id=doc_id, id=s["policySectionId"], title=s["title"], text=s["quote"]
                    )
                    for s in approved
                )
            else:
                stored = list(
                    db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == doc_id))
                )
                if len(stored) != len(approved) or any(not self.matches(doc, s) for s in stored):
                    raise RuntimeError("Published policy changed. Use an explicitly approved new version.")
        db.flush()

    def read(self, db, version, ids=None):
        docs = list(
            db.scalars(
                select(m.PolicyDocument).where(
                    m.PolicyDocument.catalog_version == version, m.PolicyDocument.status == "approved"
                )
            )
        )
        rows = []
        for doc in docs:
            sections = list(db.scalars(select(m.PolicySection).where(m.PolicySection.document_id == doc.id)))
            if not sections or not all(self.matches(doc, s) for s in sections):
                if not doc.approval_hash or doc.approval_hash != document_fingerprint(doc, sections):
                    return []
            rows.extend(
                dict(
                    policySectionId=s.id,
                    catalogVersion=version,
                    documentTitle=doc.title,
                    documentVersion=doc.version,
                    title=s.title,
                    quote=s.text,
                )
                for s in sections
            )
        if len({s["policySectionId"] for s in rows}) != len(rows):
            return []
        selected = [s for s in rows if ids is None or s["policySectionId"] in ids]
        if ids is not None and len(selected) != len(set(ids)):
            return []
        return sorted(selected, key=lambda s: s["policySectionId"])

    def reviewed(self, evidence):
        return all(
            any(
                s["catalogVersion"] == e["catalogVersion"]
                and s["policySectionId"] == e["policySectionId"]
                and s["quote"] == e["quote"]
                for s in self.sections
            )
            for e in evidence
        )

    def rules_approved(self, db, version):
        doc = db.get(m.Catalog, version)
        try:
            program = Program.model_validate_json(doc.rules_json) if doc else None
            courses = [
                course_model(c)
                for c in db.scalars(select(m.Course).where(m.Course.catalog_version == version))
            ]
            if not doc or doc.status != "published" or catalog_issues(program, courses, version):
                return False
            fingerprint = rules_fingerprint(program, courses)
            return fingerprint == self.rules_fingerprint or bool(
                doc.approval_hash and doc.approval_hash == fingerprint
            )
        except ValueError:
            return False
