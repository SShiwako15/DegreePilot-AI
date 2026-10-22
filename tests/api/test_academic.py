from dataclasses import replace

import pytest
from conftest import login

from degreepilot_api.database import load_context
from degreepilot_api.domain import audit, catalog_issues, eligibility, validate_plan
from degreepilot_api.schemas import Attempt, PlanRequest


@pytest.mark.parametrize("user,credits,gpa", [("student-ava", 35, 3.37), ("student-noah", 7, 2.64)])
def test_seed_audits(client, user, credits, gpa):
    login(client, user)
    result = client.get("/api/students/me/degree-audit").json()
    assert result["status"] == "valid"
    assert result["earnedCredits"] == credits
    assert result["gpa"] == gpa
    assert not result["degreeComplete"]
    assert result["catalogVersion"] == "NSU-BSCS-2026"


def test_categories_core_and_history(client):
    login(client)
    result = client.get("/api/students/me/degree-audit").json()
    assert [(b["earned"], b["required"]) for b in result["buckets"]] == [(23, 44), (0, 12), (12, 36), (0, 28)]
    assert sum(c["complete"] for c in result["coreRequirements"]) == 6
    assert len(client.get("/api/students/me/transcript").json()["attempts"]) == 10
    assert len(client.get("/api/catalog/NSU-BSCS-2026/courses").json()) == 36


def test_invalid_catalog_does_not_erase_transcript(client, app):
    from degreepilot_api import models as m

    login(client)
    before = client.get("/api/students/me/transcript").json()
    with app.state.sessions.begin() as db:
        db.get(m.Catalog, "NSU-BSCS-2026").rules_json = "{}"
    assert client.get("/api/students/me/degree-audit").json()["status"] == "invalid"
    assert client.get("/api/students/me/transcript").json() == before


@pytest.fixture
def context(client, app):
    with app.state.sessions() as db:
        return load_context(db, "student-ava", "2027-SPRING")


@pytest.mark.parametrize(
    "mutation",
    ["version", "duplicate", "prerequisite", "cycle", "grade_rules", "unsupplyable", "core", "pass"],
)
def test_invalid_catalogs_fail_closed(context, mutation):
    program, courses = (
        context.program.model_copy(deep=True),
        [c.model_copy(deep=True) for c in context.courses],
    )
    if mutation == "version":
        program.catalog_version = "OTHER"
    elif mutation == "duplicate":
        courses.append(courses[0])
    elif mutation == "prerequisite":
        courses[0].prerequisite_groups = [["UNKNOWN"]]
    elif mutation == "cycle":
        courses[0].prerequisite_groups = [[courses[0].code]]
    elif mutation == "grade_rules":
        program.min_passing_grade = "D"
    elif mutation == "unsupplyable":
        program.min_total_credits = 9999
    elif mutation == "core":
        program.core_course_codes.append("UNKNOWN")
    elif mutation == "pass":
        program.pass_credit_categories = ["core"]
    assert catalog_issues(program, courses, context.version)
    result = audit(program, courses, context.attempts, context.version)
    assert result["status"] == "invalid" and result["earnedCredits"] is None


@pytest.mark.parametrize(
    "attempt",
    [
        Attempt(course_code="UNKNOWN", term="2026-FALL", grade="A"),
        Attempt(course_code="CS101", term="bad", grade="A"),
        Attempt(course_code="CS101", term="2026-FALL", grade="X"),
    ],
)
def test_invalid_transcripts_fail_closed(context, attempt):
    assert audit(context.program, context.courses, [attempt], context.version)["status"] == "invalid"


def test_repeat_grades_and_pass_policy(context):
    attempts = [
        Attempt(course_code="CS101", term="2025-FALL", grade="A"),
        Attempt(course_code="CS101", term="2026-SPRING", grade="C"),
        Attempt(course_code="CS101", term="2026-FALL", grade="F"),
    ]
    result = audit(context.program, context.courses, attempts, context.version)
    assert result["earnedCredits"] == 4
    assert result["earnedCourses"][0]["grade"] == "C"
    assert result["gpa"] == 2.0
    attempts = [Attempt(course_code="CS101", term="2026-FALL", grade="P")]
    assert audit(context.program, context.courses, attempts, context.version)["earnedCredits"] == 0
    assert (
        audit(context.program, context.courses, [attempts[0], attempts[0]], context.version)["status"]
        == "invalid"
    )


def test_eligibility_and_prior_term_prerequisites(context):
    assert eligibility(context, "CS301")["eligible"]
    result = eligibility(context, "CS401")
    assert not result["eligible"] and result["missingPrerequisiteGroups"] == [["CS301"], ["CS310"]]
    same_term = replace(
        context, attempts=context.attempts + [Attempt(course_code="CS301", term="2027-SPRING", grade="A")]
    )
    assert ["CS301"] in eligibility(same_term, "CS401")["missingPrerequisiteGroups"]
    assert eligibility(context, "CS101")["status"] == "completed"


def test_invalid_sections_and_conflicts(context):
    request = PlanRequest(term_code="2027-SPRING", min_credits=6, max_credits=6)
    ids = [
        next(s.id for s in context.sections if s.section_code == code) for code in ("CS220-01", "CS301-01")
    ]
    result = validate_plan(context, request, ids)
    assert result["status"] == "INVALID" and any(i["code"] == "TIME_CONFLICT" for i in result["issues"])
    assert validate_plan(context, request, [ids[0], ids[0]])["status"] == "INVALID"
    assert validate_plan(context, request, ["unknown"])["status"] == "INVALID"
    corrupted = replace(context, sections=[context.sections[0].model_copy(update={"available_seats": -1})])
    assert eligibility(corrupted, "CS301")["status"] == "invalid"
