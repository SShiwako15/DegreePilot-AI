from copy import deepcopy

import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.degree_models import DegreeReply, DegreeRequest, DegreeSelection
from app.degree_solver import greedy_degree, solve_degree
from app.degree_validator import validate_degree
from app.main import app


def problem(horizon=4):
    """Taking the alphabetically earlier capstone in Fall postpones the Fall-only course."""
    courses = [
        dict(code="A001", credits=3, category="core", prerequisiteGroups=[]),
        dict(code="B200", credits=3, category="core", prerequisiteGroups=[["A001"]]),
        dict(code="Z999", credits=3, category="core", prerequisiteGroups=[]),
    ]
    terms = []
    for t in range(horizon):
        code = f"{2027 + t // 2}-{'SPRING' if t % 2 == 0 else 'FALL'}"
        options = [
            dict(id=f"tentative:{c['code']}", courseCode=c["code"], credits=3, core=True, days=[])
            for c in courses
            if c["code"] != "Z999" or t % 2 == 1
        ]
        terms.append(dict(code=code, published=False, options=options))
    return DegreeRequest(
        catalogVersion="SYNTHETIC-TRAP",
        courses=courses,
        earned=[],
        passed=[],
        requiredCore=[c["code"] for c in courses],
        categoryMinimums={"core": 9, "cs_elective": 0, "gen_ed": 0, "free_elective": 0},
        minTotalCredits=9,
        terms=terms,
        minCredits=3,
        maxCredits=3,
    )


def test_bounded_cpsat_beats_documented_greedy_and_both_replay():
    request = problem()
    cp, baseline = solve_degree(request), greedy_degree(request)
    assert cp.status == baseline.status == "FEASIBLE"
    assert cp.optimal and cp.completionTerms == 3 and baseline.completionTerms == 4
    assert validate_degree(request, cp) == validate_degree(request, baseline) == []
    short = problem(3)
    assert solve_degree(short).status == "FEASIBLE"
    assert greedy_degree(short).status == "NO_SOLUTION"  # Cannot claim INFEASIBLE.


def test_infeasible_timeout_and_completed_statuses_are_distinct():
    short = problem(1)
    assert solve_degree(short).status == "INFEASIBLE"
    request = problem().model_copy(update={"maxTimeSeconds": 0.0})
    assert solve_degree(request).status == "TIMEOUT"
    done = problem().model_copy(
        update={"passed": ["A001", "B200", "Z999"], "earned": ["A001", "B200", "Z999"]}
    )
    result = solve_degree(done)
    assert result.completionTerms == 0 and result.selections == []
    assert validate_degree(done, result) == []


@pytest.mark.parametrize("attack", ["duplicate", "same-term", "unknown", "load"])
def test_replay_rejects_tampered_schedules(attack):
    request = problem()
    result = solve_degree(request)
    data = result.model_dump()
    first = data["selections"][0]
    if attack == "duplicate":
        first["selectionIds"] *= 2
    if attack == "same-term":
        first["selectionIds"] = ["tentative:B200"]
    if attack == "unknown":
        first["selectionIds"] = ["made-up"]
    if attack == "load":
        request = request.model_copy(update={"minCredits": 6})
    assert validate_degree(request, DegreeReply.model_validate(data))


def test_and_of_or_uses_strictly_earlier_terms():
    data = problem().model_dump()
    data["courses"][1]["prerequisiteGroups"] = [["A001", "Z999"]]
    request = DegreeRequest.model_validate(data)
    result = solve_degree(request)
    assert result.status == "FEASIBLE" and validate_degree(request, result) == []
    bad = DegreeReply(
        status="FEASIBLE",
        completionTerms=1,
        selections=[
            DegreeSelection(termCode="2027-SPRING", selectionIds=["tentative:A001", "tentative:B200"])
        ],
    )
    assert "MISSING_PREREQUISITE" in validate_degree(request, bad)


def test_cycles_scope_and_duplicate_earned_credits_rejected_at_boundary():
    data = problem().model_dump()
    for mutation in ("cycle", "earned", "scope"):
        changed = deepcopy(data)
        if mutation == "cycle":
            changed["courses"][0]["prerequisiteGroups"] = [["B200"]]
        elif mutation == "earned":
            changed["earned"] = ["A001", "A001"]
        else:
            changed["terms"][0]["options"][0]["courseCode"] = "OTHER-CATALOG"
        with pytest.raises(ValidationError):
            DegreeRequest.model_validate(changed)


def test_published_conflicts_adjacency_and_closed_sections_not_projected():
    data = problem(2).model_dump()
    data["courses"] = [
        dict(code=c, credits=3, category="core", prerequisiteGroups=[]) for c in ("A001", "B200")
    ]
    data["requiredCore"] = ["A001", "B200"]
    data["categoryMinimums"]["core"] = data["minTotalCredits"] = 6
    data["maxCredits"] = 6
    data["terms"][1]["options"] = data["terms"][1]["options"][:2]
    options = [
        dict(id=i, courseCode=c, credits=3, core=True, days=["Mon"], startMinute=start, endMinute=end)
        for i, c, start, end in [("a1", "A001", 540, 600), ("a2", "A001", 600, 660), ("b1", "B200", 540, 600)]
    ]
    data["terms"][0] = dict(code="2027-SPRING", published=True, options=options)
    request = DegreeRequest.model_validate(data)
    result = solve_degree(request)
    assert result.completionTerms == 1
    assert set(result.selections[0].selectionIds) == {"a2", "b1"}
    assert validate_degree(request, result) == []
    data["terms"] = [dict(code="2027-SPRING", published=True, options=[])]
    assert solve_degree(DegreeRequest.model_validate(data)).status == "INFEASIBLE"


def test_internal_api_runs_without_ai_and_rejects_unbounded_request():
    client = TestClient(app)
    assert client.post("/degree/solve", json=problem().model_dump()).json()["status"] == "FEASIBLE"
    data = problem().model_dump()
    data["maxTimeSeconds"] = 99.0
    assert client.post("/degree/solve", json=data).status_code == 422
