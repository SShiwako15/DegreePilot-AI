from datetime import date
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator
from pydantic.alias_generators import to_camel


class Model(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True, alias_generator=to_camel, populate_by_name=True)


class Program(Model):
    institution: str
    catalog_version: str
    program_id: str
    program_name: str
    effective_year: int
    min_total_credits: int
    core_course_codes: list[str]
    min_cs_elective_credits: int
    min_gen_ed_credits: int
    min_free_elective_credits: int
    min_passing_grade: str
    grade_point_scale: dict[str, int]
    grade_policy_document: str
    notes: str | None = None
    pass_credit_categories: list[str] = Field(default_factory=list)


class Course(Model):
    code: str
    title: str
    credits: int
    category: str
    prerequisite_groups: list[list[str]]
    offered_terms: list[str]
    catalog_version: str


class Attempt(Model):
    course_code: str
    term: str
    grade: str


class Term(Model):
    code: str
    name: str
    kind: str
    starts_on: date
    ends_on: date
    timezone: str
    is_published: bool

    @field_validator("starts_on", "ends_on", mode="before")
    @classmethod
    def iso_date(cls, value):
        if isinstance(value, str):
            return date.fromisoformat(value)
        return value


class Section(Model):
    id: str
    catalog_version: str
    term_code: str
    course_code: str
    section_code: str
    days: list[str]
    start_local: str
    end_local: str
    timezone: str
    capacity: int
    available_seats: int
    delivery: str


class PlanRequest(Model):
    term_code: str = Field(pattern=r"^[0-9]{4}-(SPRING|FALL)$")
    min_credits: int = Field(default=12, ge=1, le=30, strict=True)
    max_credits: int = Field(default=18, ge=1, le=30, strict=True)
    preferred_days: list[Literal["Mon", "Tue", "Wed", "Thu", "Fri"]] | None = Field(
        default=None, max_length=5
    )
    fewer_campus_days: bool = True

    @model_validator(mode="after")
    def ordered_bounds(self):
        if self.min_credits > self.max_credits:
            raise ValueError("Reversed credit bounds")
        return self


class SelectionRequest(Model):
    constraints: PlanRequest
    selection_ids: list[str] = Field(max_length=30)


class DegreeRequest(Model):
    start_term_code: str = Field(pattern=r"^[0-9]{4}-(SPRING|FALL)$")
    horizon_terms: int = Field(default=8, ge=1, le=12, strict=True)
    min_credits: int = Field(default=12, ge=1, le=30, strict=True)
    max_credits: int = Field(default=18, ge=1, le=30, strict=True)

    @model_validator(mode="after")
    def ordered_bounds(self):
        if self.min_credits > self.max_credits:
            raise ValueError("Reversed credit bounds")
        return self


class LoginRequest(Model):
    username: str = Field(min_length=1, max_length=100)
    password: str = Field(min_length=1, max_length=256)


class ChatRequest(Model):
    question: str = Field(min_length=1, max_length=1000)
    catalog_version: str | None = Field(default=None, max_length=100)
    term_code: str | None = Field(default=None, pattern=r"^[0-9]{4}-(SPRING|FALL)$")
    previous_questions: list[str] = Field(default_factory=list, max_length=4)

    @model_validator(mode="after")
    def valid_questions(self):
        if not self.question.strip() or any(not q.strip() or len(q) > 1000 for q in self.previous_questions):
            raise ValueError("Invalid question")
        return self


class AssignmentRequest(Model):
    advisor_id: str
    student_id: str
    active: bool
    revision: int = Field(ge=0)


class CatalogClone(Model):
    source_version: str
    catalog_version: str = Field(pattern=r"^[A-Z0-9][A-Z0-9-]{2,79}$")


class CatalogDraft(Model):
    revision: int
    program: Program
    courses: list[Course] = Field(min_length=1, max_length=200)


class Revision(Model):
    revision: int


class PolicyClone(Model):
    source_document_id: str


class PolicyText(Model):
    id: str = Field(pattern=r"^[A-Z0-9][A-Z0-9-]{2,79}$")
    title: str = Field(min_length=1, max_length=200)
    text: str = Field(min_length=1, max_length=3000)


class PolicyDraft(Model):
    revision: int
    title: str = Field(min_length=1, max_length=200)
    sections: list[PolicyText] = Field(min_length=1, max_length=30)


class SectionSeats(Model):
    catalog_version: str
    section_id: str
    capacity: int = Field(ge=1, le=1000)
    available_seats: int = Field(ge=0)
    expected_capacity: int
    expected_available_seats: int
