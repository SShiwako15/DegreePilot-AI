"""Mappings retain the existing PostgreSQL schema and its foreign keys."""

from datetime import date, datetime
from uuid import UUID

from sqlalchemy import (
    CheckConstraint,
    DateTime,
    ForeignKey,
    ForeignKeyConstraint,
    Index,
    Text,
    UniqueConstraint,
    event,
)
from sqlalchemy.orm import DeclarativeBase, Mapped, Session, mapped_column


class Base(DeclarativeBase):
    type_annotation_map = {str: Text, datetime: DateTime(timezone=True)}


class User(Base):
    __tablename__ = "Users"
    id: Mapped[str] = mapped_column("Id", primary_key=True)
    display_name: Mapped[str] = mapped_column("DisplayName")
    email: Mapped[str] = mapped_column("Email", unique=True)
    role: Mapped[str] = mapped_column("Role")
    password_hash: Mapped[str] = mapped_column("PasswordHash")
    __table_args__ = (CheckConstraint("\"Role\" IN ('student','advisor','admin')", name="CK_User_Role"),)


class Catalog(Base):
    __tablename__ = "Catalogs"
    id: Mapped[str] = mapped_column("Id", primary_key=True)
    program_id: Mapped[str] = mapped_column("ProgramId")
    rules_json: Mapped[str] = mapped_column("RulesJson")
    source_hash: Mapped[str] = mapped_column("SourceHash", default="")
    status: Mapped[str] = mapped_column("Status", default="published")
    revision: Mapped[int] = mapped_column("Revision", default=1)
    approval_hash: Mapped[str] = mapped_column("ApprovalHash", default="")
    __mapper_args__ = {"version_id_col": revision, "version_id_generator": False}
    __table_args__ = (CheckConstraint("\"Status\" IN ('draft','published')", name="CK_Catalog_Status"),)


class Course(Base):
    __tablename__ = "Courses"
    catalog_version: Mapped[str] = mapped_column(
        "CatalogVersion", ForeignKey("Catalogs.Id"), primary_key=True
    )
    code: Mapped[str] = mapped_column("Code", primary_key=True)
    title: Mapped[str] = mapped_column("Title")
    credits: Mapped[int] = mapped_column("Credits")
    category: Mapped[str] = mapped_column("Category")
    prerequisites_json: Mapped[str] = mapped_column("PrerequisitesJson")
    offered_terms_json: Mapped[str] = mapped_column("OfferedTermsJson")
    __table_args__ = (
        CheckConstraint('"Credits" > 0 AND "Credits" <= 30', name="CK_Course_Credits"),
        CheckConstraint(
            "\"Category\" IN ('core','cs_elective','gen_ed','free_elective')", name="CK_Course_Category"
        ),
    )


class Student(Base):
    __tablename__ = "Students"
    user_id: Mapped[str] = mapped_column("UserId", ForeignKey("Users.Id"), primary_key=True)
    catalog_version: Mapped[str] = mapped_column("CatalogVersion", ForeignKey("Catalogs.Id"))
    __table_args__ = (UniqueConstraint("UserId", "CatalogVersion"),)


class Attempt(Base):
    __tablename__ = "Attempts"
    id: Mapped[UUID] = mapped_column("Id", primary_key=True)
    student_id: Mapped[str] = mapped_column("StudentId")
    catalog_version: Mapped[str] = mapped_column("CatalogVersion")
    course_code: Mapped[str] = mapped_column("CourseCode")
    term: Mapped[str] = mapped_column("Term")
    grade: Mapped[str] = mapped_column("Grade")
    __table_args__ = (
        ForeignKeyConstraint(["StudentId", "CatalogVersion"], ["Students.UserId", "Students.CatalogVersion"]),
        ForeignKeyConstraint(["CatalogVersion", "CourseCode"], ["Courses.CatalogVersion", "Courses.Code"]),
        UniqueConstraint("StudentId", "CourseCode", "Term"),
        CheckConstraint("\"Grade\" IN ('A','B','C','D','F','W','I','P')", name="CK_Attempt_Grade"),
    )


class Term(Base):
    __tablename__ = "Terms"
    code: Mapped[str] = mapped_column("Code", primary_key=True)
    name: Mapped[str] = mapped_column("Name")
    kind: Mapped[str] = mapped_column("Kind")
    starts_on: Mapped[date] = mapped_column("StartsOn")
    ends_on: Mapped[date] = mapped_column("EndsOn")
    timezone: Mapped[str] = mapped_column("Timezone")
    is_published: Mapped[bool] = mapped_column("IsPublished")
    __table_args__ = (
        CheckConstraint('"StartsOn" < "EndsOn"', name="CK_Term_Dates"),
        CheckConstraint("\"Kind\" IN ('Spring','Fall')", name="CK_Term_Kind"),
    )


class Section(Base):
    __tablename__ = "Sections"
    catalog_version: Mapped[str] = mapped_column("CatalogVersion", primary_key=True)
    id: Mapped[str] = mapped_column("Id", primary_key=True)
    term_code: Mapped[str] = mapped_column("TermCode", ForeignKey("Terms.Code"))
    course_code: Mapped[str] = mapped_column("CourseCode")
    section_code: Mapped[str] = mapped_column("SectionCode")
    days_json: Mapped[str] = mapped_column("DaysJson")
    start_local: Mapped[str] = mapped_column("StartLocal")
    end_local: Mapped[str] = mapped_column("EndLocal")
    timezone: Mapped[str] = mapped_column("Timezone")
    capacity: Mapped[int] = mapped_column("Capacity")
    available_seats: Mapped[int] = mapped_column("AvailableSeats")
    delivery: Mapped[str] = mapped_column("Delivery")
    __table_args__ = (
        ForeignKeyConstraint(["CatalogVersion", "CourseCode"], ["Courses.CatalogVersion", "Courses.Code"]),
        UniqueConstraint("CatalogVersion", "TermCode", "SectionCode"),
        CheckConstraint(
            '"Capacity" > 0 AND "AvailableSeats" >= 0 AND "AvailableSeats" <= "Capacity"',
            name="CK_Section_Seats",
        ),
        CheckConstraint('"StartLocal" < "EndLocal"', name="CK_Section_Times"),
    )


class Plan(Base):
    __tablename__ = "Plans"
    id: Mapped[UUID] = mapped_column("Id", primary_key=True)
    student_id: Mapped[str] = mapped_column("StudentId")
    catalog_version: Mapped[str] = mapped_column("CatalogVersion")
    term_code: Mapped[str] = mapped_column("TermCode", ForeignKey("Terms.Code"))
    constraints_json: Mapped[str] = mapped_column("ConstraintsJson")
    selection_ids_json: Mapped[str] = mapped_column("SelectionIdsJson")
    created_at: Mapped[datetime] = mapped_column("CreatedAt")
    __table_args__ = (
        ForeignKeyConstraint(["StudentId", "CatalogVersion"], ["Students.UserId", "Students.CatalogVersion"]),
    )


class PolicyDocument(Base):
    __tablename__ = "PolicyDocuments"
    id: Mapped[str] = mapped_column("Id", primary_key=True)
    catalog_version: Mapped[str] = mapped_column("CatalogVersion", ForeignKey("Catalogs.Id"))
    title: Mapped[str] = mapped_column("Title")
    version: Mapped[int] = mapped_column("Version")
    status: Mapped[str] = mapped_column("Status", default="draft")
    source_hash: Mapped[str] = mapped_column("SourceHash", default="")
    replaces_document_id: Mapped[str | None] = mapped_column(
        "ReplacesDocumentId", ForeignKey("PolicyDocuments.Id")
    )
    approval_hash: Mapped[str] = mapped_column("ApprovalHash", default="")
    revision: Mapped[int] = mapped_column("Revision", default=1)
    __mapper_args__ = {"version_id_col": revision, "version_id_generator": False}
    __table_args__ = (
        CheckConstraint("\"Status\" IN ('draft','approved','retired')", name="CK_Policy_Status"),
        CheckConstraint('"Version" > 0', name="CK_Policy_Version"),
    )


class PolicySection(Base):
    __tablename__ = "PolicySections"
    document_id: Mapped[str] = mapped_column("DocumentId", ForeignKey("PolicyDocuments.Id"), primary_key=True)
    id: Mapped[str] = mapped_column("Id", primary_key=True)
    title: Mapped[str] = mapped_column("Title")
    text: Mapped[str] = mapped_column("Text")


class Assignment(Base):
    __tablename__ = "AdvisorAssignments"
    advisor_id: Mapped[str] = mapped_column("AdvisorId", ForeignKey("Users.Id"), primary_key=True)
    student_id: Mapped[str] = mapped_column("StudentId", ForeignKey("Students.UserId"), primary_key=True)
    active: Mapped[bool] = mapped_column("Active")
    revision: Mapped[int] = mapped_column("Revision", default=1)
    __mapper_args__ = {"version_id_col": revision, "version_id_generator": False}


class Suggestion(Base):
    __tablename__ = "AdvisorSuggestions"
    id: Mapped[UUID] = mapped_column("Id", primary_key=True)
    advisor_id: Mapped[str] = mapped_column("AdvisorId", ForeignKey("Users.Id"))
    student_id: Mapped[str] = mapped_column("StudentId")
    catalog_version: Mapped[str] = mapped_column("CatalogVersion")
    term_code: Mapped[str] = mapped_column("TermCode", ForeignKey("Terms.Code"))
    constraints_json: Mapped[str] = mapped_column("ConstraintsJson")
    selection_ids_json: Mapped[str] = mapped_column("SelectionIdsJson")
    created_at: Mapped[datetime] = mapped_column("CreatedAt")
    __table_args__ = (
        ForeignKeyConstraint(["StudentId", "CatalogVersion"], ["Students.UserId", "Students.CatalogVersion"]),
    )


class MutationAudit(Base):
    __tablename__ = "MutationAudits"
    id: Mapped[UUID] = mapped_column("Id", primary_key=True)
    actor_id: Mapped[str] = mapped_column("ActorId", ForeignKey("Users.Id"))
    action: Mapped[str] = mapped_column("Action")
    resource_id: Mapped[str] = mapped_column("ResourceId")
    revision: Mapped[int] = mapped_column("Revision")
    content_hash: Mapped[str] = mapped_column("ContentHash")
    occurred_at: Mapped[datetime] = mapped_column("OccurredAt")
    __table_args__ = (Index("IX_MutationAudits_OccurredAt", "OccurredAt"),)


@event.listens_for(Session, "before_flush")
def protect_audit_history(session, _context, _instances):
    if any(isinstance(row, MutationAudit) for row in session.dirty | session.deleted):
        raise ValueError("Mutation audit entries are append-only.")
