from itertools import combinations

import pytest
from fastapi.testclient import TestClient
from ortools.sat.python import cp_model

from app.main import app
from app.models import Option, SolveRequest
from app.solver import decode_status, overlaps, solve

client = TestClient(app)


def option(id, code="CS301", day="Tue", start=540, end=615, credits=3):
    return Option(
        id=id, courseCode=code, credits=credits, core=True, days=[day], startMinute=start, endMinute=end
    )


@pytest.mark.parametrize(
    "other,expected",
    [
        (option("b", start=615, end=690), False),
        (option("b", start=600, end=675), True),
        (option("b", day="Wed"), False),
    ],
)
def test_half_open_meetings(other, expected):
    assert overlaps(option("a"), other) is expected


def test_real_solver_excludes_conflicts_and_duplicate_course_sections():
    choices = [
        option("algo1"),
        option("algo2", start=780, end=855),
        option("engineering", code="CS220"),
        option("os", code="CS310", day="Mon"),
    ]
    request = SolveRequest(options=choices, minCredits=9, maxCredits=9)
    result = solve(request)
    assert result.status == "FEASIBLE"
    selected = [o for o in choices if o.id in result.selectionIds]
    assert sum(o.credits for o in selected) == 9
    assert len({o.courseCode for o in selected}) == len(selected)
    assert all(not overlaps(a, b) for a, b in combinations(selected, 2))
    assert "algo2" in result.selectionIds


def test_infeasible_means_proven_no_solution():
    result = solve(SolveRequest(options=[option("a"), option("b", code="CS220")], minCredits=6, maxCredits=6))
    assert result.status == "INFEASIBLE"
    assert result.selectionIds == []


def test_zero_budget_actual_solver_is_timeout_not_infeasible():
    result = solve(SolveRequest(options=[option("a")], minCredits=3, maxCredits=3, maxTimeSeconds=0.0))
    assert result.status == "TIMEOUT"
    assert result.selectionIds == []


@pytest.mark.parametrize(
    "status,expected",
    [
        (cp_model.UNKNOWN, "TIMEOUT"),
        (cp_model.INFEASIBLE, "INFEASIBLE"),
        (cp_model.MODEL_INVALID, "ERROR"),
        (cp_model.FEASIBLE, "FEASIBLE"),
    ],
)
def test_solver_status_is_not_invented(status, expected):
    assert decode_status(status) == expected


def test_preferences_are_soft_and_tentative_courses_have_no_times():
    tentative = Option(id="tentative:CS301", courseCode="CS301", credits=3, core=True, days=[])
    assert solve(SolveRequest(options=[tentative], minCredits=3, maxCredits=3)).status == "FEASIBLE"
    assert (
        solve(SolveRequest(options=[option("a")], minCredits=3, maxCredits=3, preferredDays=["Fri"])).status
        == "FEASIBLE"
    )


def test_http_validation_health_and_minimal_contract():
    assert client.get("/health").status_code == 200
    request = SolveRequest(options=[option("a")], minCredits=3, maxCredits=3)
    result = client.post("/solve", json=request.model_dump())
    assert result.status_code == 200
    assert result.json() == {"status": "FEASIBLE", "selectionIds": ["a"]}
    invalid = request.model_dump() | {"studentId": "student-ava"}
    assert client.post("/solve", json=invalid).status_code == 422
    assert client.post("/solve", json=request.model_dump() | {"maxTimeSeconds": 100}).status_code == 422
    assert client.post("/solve", json=request.model_dump() | {"minCredits": 10}).status_code == 422
