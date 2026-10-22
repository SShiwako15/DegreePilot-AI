import json
import os
from datetime import date
from pathlib import Path

import pytest
from conftest import login, post
from sqlalchemy import select

from degreepilot_api import models as m
from degreepilot_api.database import load_context
from degreepilot_api.domain import GRADE_POINTS, Context, validate_plan
from degreepilot_api.planning import validate_degree
from degreepilot_api.schemas import Attempt, Course, DegreeRequest, PlanRequest, Program, Section, Term
from degreepilot_api.settings import ROOT

DATASET = Path(os.getenv("DEGREE_BENCHMARK_DIR", ROOT / "benchmarks/degree_planning"))


def dataset(name):
    return json.loads((DATASET / name).read_text(encoding="utf-8"))


def ava_reply():
    return next(
        t["result"]
        for t in dataset("traces.json")
        if t["case_id"] == "student-ava-2027-SPRING-8-18" and t["algorithm"] == "cp_sat"
    )


def valid_ids(db):
    from itertools import combinations

    from degreepilot_api.domain import options

    context = load_context(db, "student-ava", "2027-SPRING")
    request = PlanRequest(term_code=context.term.code, min_credits=12, max_credits=12)
    for choices in combinations(options(context), 4):
        ids = [c["id"] for c in choices]
        if validate_plan(context, request, ids)["status"] == "FEASIBLE":
            return ids
    raise AssertionError("Expected a fixture plan")


def test_solver_proposal_save_ownership_and_revalidation(client, app):
    login(client)
    with app.state.sessions() as db:
        ids = valid_ids(db)
    app.state.gateway.reply = dict(status="FEASIBLE", selectionIds=ids)
    constraints = dict(termCode="2027-SPRING", minCredits=12, maxCredits=12)
    result = post(client, "/api/plans/generate", constraints).json()
    assert result["status"] == "FEASIBLE" and result["totalCredits"] == 12
    saved = post(client, "/api/plans/save", dict(constraints=constraints, selectionIds=ids))
    assert saved.status_code == 200 and saved.json()["id"]
    plan_id = saved.json()["id"]
    assert client.get(f"/api/plans/{plan_id}").json()["status"] == "FEASIBLE"
    with app.state.sessions.begin() as db:
        db.get(m.Section, ("NSU-BSCS-2026", ids[0])).available_seats = 0
        assert len(list(db.scalars(select(m.Attempt)))) == 13
    assert client.get(f"/api/plans/{plan_id}").json()["status"] == "INVALID"
    post(client, "/api/auth/logout")
    login(client, "student-noah")
    assert client.get(f"/api/plans/{plan_id}").status_code == 404


@pytest.mark.parametrize(
    "reply,status",
    [
        (None, "UNAVAILABLE"),
        ({"status": "TIMEOUT", "selectionIds": []}, "TIMEOUT"),
        ({"status": "INFEASIBLE", "selectionIds": []}, "INFEASIBLE"),
        ({"status": "FEASIBLE", "selectionIds": ["invented"]}, "ERROR"),
        ({"status": "INFEASIBLE", "selectionIds": ["invented"]}, "ERROR"),
        ({"status": "FEASIBLE", "selectionIds": None}, "ERROR"),
    ],
)
def test_untrusted_solver_failures(client, app, reply, status):
    login(client)
    app.state.gateway.reply = reply
    result = post(client, "/api/plans/generate", {"termCode": "2027-SPRING"}).json()
    assert result["status"] == status and result["selections"] == []


def test_tentative_plan_and_future_guard(client):
    login(client)
    data = client.get("/api/terms/2028-SPRING/sections").json()
    assert data["term"]["isPublished"] is False and data["sections"] == []
    assert all(o["tentative"] and o["startLocal"] is None for o in data["options"])


def test_advisor_suggestions_are_separate_from_saved_plans(client, app):
    with app.state.sessions() as db:
        ids = valid_ids(db)
    login(client, "advisor-morgan")
    body = dict(constraints=dict(termCode="2027-SPRING", minCredits=12, maxCredits=12), selectionIds=ids)
    assert post(client, "/api/advisor-workspace/students/student-noah/suggestions", body).status_code == 404
    assert post(client, "/api/advisor-workspace/students/student-ava/suggestions", body).status_code == 200
    assert len(client.get("/api/students/student-ava/suggestions").json()) == 1
    assert client.get("/api/students/student-ava/plans").json() == []


def test_degree_roadmap_does_not_award_credits(client, app):
    login(client)
    app.state.gateway.reply = ava_reply()
    result = post(client, "/api/degree-plans/generate", {"startTermCode": "2027-SPRING"}).json()
    assert result["status"] == "FEASIBLE", result
    assert result["conditionalDegreeComplete"] and len(result["inputFingerprint"]) == 64
    assert client.get("/api/students/me/degree-audit").json()["earnedCredits"] == 35
    with app.state.sessions() as db:
        assert len(list(db.scalars(select(m.Attempt)))) == 13
        assert db.scalar(select(m.MutationAudit.action)) == "degree-plan.generate"


def test_degree_rejects_stale_data_and_cross_student_results(client, app):
    login(client)
    app.state.gateway.reply = ava_reply()

    def change():
        with app.state.sessions.begin() as db:
            db.scalar(select(m.Section)).available_seats = 0

    app.state.gateway.before_reply = change
    result = post(client, "/api/degree-plans/generate", {"startTermCode": "2027-SPRING"}).json()
    assert result["status"] == "ERROR" and result["issues"][0]["code"] == "PLANNING_DATA_CHANGED"
    app.state.gateway.before_reply = None
    post(client, "/api/auth/logout")
    login(client, "student-noah")
    assert (
        post(client, "/api/degree-plans/generate", {"startTermCode": "2027-SPRING"}).json()["status"]
        == "ERROR"
    )


def benchmark_context(problem):
    courses = [
        Course(
            code=c["code"],
            title=c["code"],
            credits=c["credits"],
            category=c["category"],
            prerequisite_groups=c["prerequisiteGroups"],
            catalog_version=problem["catalogVersion"],
            offered_terms=list(
                {
                    "Fall" if t["code"].endswith("FALL") else "Spring"
                    for t in problem["terms"]
                    if any(o["courseCode"] == c["code"] for o in t["options"])
                }
            ),
        )
        for c in problem["courses"]
    ]
    program = Program(
        institution="Synthetic benchmark",
        catalog_version=problem["catalogVersion"],
        program_id="TEST",
        program_name="Synthetic degree",
        effective_year=2026,
        min_total_credits=problem["minTotalCredits"],
        core_course_codes=problem["requiredCore"],
        min_cs_elective_credits=problem["categoryMinimums"]["cs_elective"],
        min_gen_ed_credits=problem["categoryMinimums"]["gen_ed"],
        min_free_elective_credits=problem["categoryMinimums"]["free_elective"],
        min_passing_grade="C",
        grade_point_scale=GRADE_POINTS,
        grade_policy_document="SYNTHETIC",
        pass_credit_categories=["cs_elective", "gen_ed", "free_elective"],
    )
    attempts = [
        Attempt(course_code=c, term="2026-FALL", grade="C" if c in problem["passed"] else "P")
        for c in problem["earned"]
    ]
    contexts = []
    for t in problem["terms"]:
        year, fall = int(t["code"][:4]), t["code"].endswith("FALL")
        term = Term(
            code=t["code"],
            name=t["code"],
            kind="Fall" if fall else "Spring",
            starts_on=date(year, 8 if fall else 1, 15),
            ends_on=date(year, 12 if fall else 5, 15),
            timezone="America/New_York",
            is_published=t["published"],
        )
        sections = []
        for o in t["options"] if t["published"] else []:

            def clock(n):
                return f"{n // 60:02d}:{n % 60:02d}"

            sections.append(
                Section(
                    id=o["id"],
                    catalog_version=problem["catalogVersion"],
                    term_code=t["code"],
                    course_code=o["courseCode"],
                    section_code=o["id"],
                    days=o["days"],
                    start_local=clock(o["startMinute"]),
                    end_local=clock(o["endMinute"]),
                    timezone=term.timezone,
                    capacity=1,
                    available_seats=1,
                    delivery="synthetic",
                )
            )
        contexts.append(Context(problem["catalogVersion"], program, courses, attempts, term, sections))
    return contexts


def test_every_feasible_benchmark_trace_passes_independent_api_rules():
    cases = {c["id"]: c["request"] for c in dataset("cases.json")}
    checked = 0
    for trace in dataset("traces.json"):
        reply = trace["result"]
        if reply["status"] != "FEASIBLE":
            assert not reply["selections"]
            continue
        problem = cases[trace["case_id"]]
        request = DegreeRequest(
            start_term_code=problem["terms"][0]["code"],
            horizon_terms=len(problem["terms"]),
            min_credits=problem["minCredits"],
            max_credits=problem["maxCredits"],
        )
        result = validate_degree(benchmark_context(problem), request, reply, "benchmark")
        assert result["status"] == "FEASIBLE", (trace["case_id"], result["issues"])
        checked += 1
    summary = dataset("manifest.json")["summary"]
    assert checked == summary["cp_sat"]["feasible"] + summary["greedy"]["feasible"]


def test_independent_validator_rejects_nonfeasible_status():
    problem = next(c["request"] for c in dataset("cases.json") if c["id"] == "student-ava-2027-SPRING-8-18")
    request = DegreeRequest(start_term_code="2027-SPRING")
    reply = {**ava_reply(), "status": "INFEASIBLE"}
    result = validate_degree(benchmark_context(problem), request, reply, "benchmark")
    assert result["status"] == "ERROR" and result["terms"] == []
