"""Reproduce synthetic-only degree-planning comparisons; no network or student database."""
# ruff: noqa: E402 -- resolve the repository's private package before importing it

import argparse
import csv
import hashlib
import json
import os
import platform
import statistics
import sys
import time
from datetime import UTC, datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "services/ai"))

import ortools

from app.degree_models import DegreeRequest
from app.degree_solver import greedy_degree, solve_degree
from app.degree_validator import validate_degree


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def fixture_case(student, start, horizon, maximum, repeated=False):
    program = json.loads((ROOT / "data/program.json").read_text())
    courses = json.loads((ROOT / "data/courses.json").read_text())
    users = json.loads((ROOT / "data/students.json").read_text())
    transcript = next(u["transcript"] for u in users if u["id"] == student)
    if repeated:
        transcript = transcript + [dict(course_code="CS101", grade="D", term="2024-SPRING")]
    passed = sorted({a["course_code"] for a in transcript if a["grade"] in ("A", "B", "C")})
    earned = set(passed)
    approved_pass = program.get("pass_credit_categories", [])
    by_code = {c["code"]: c for c in courses}
    earned |= {
        a["course_code"]
        for a in transcript
        if a["grade"] == "P" and by_code[a["course_code"]]["category"] in approved_pass
    }
    # Fixture gold values are a gate, not a benchmark accuracy estimate.
    assert sum(by_code[c]["credits"] for c in earned) == (35 if student == "student-ava" else 7)
    published = {t["code"]: t["is_published"] for t in json.loads((ROOT / "data/terms.json").read_text())}
    sections = json.loads((ROOT / "data/sections.json").read_text())
    order = int(start[:4]) * 2 + int(start.endswith("FALL"))
    terms = []
    for i in range(horizon):
        year, fall = divmod(order + i, 2)
        kind = "Fall" if fall else "Spring"
        code = f"{year}-{'FALL' if fall else 'SPRING'}"
        is_published = published.get(code, False)
        options = []
        if is_published:
            for section in sections:
                if section["term_code"] != code or section["available_seats"] <= 0:
                    continue
                c = by_code[section["course_code"]]

                def minute(s):
                    return int(s[:2]) * 60 + int(s[3:])

                options.append(
                    dict(
                        id=section["id"],
                        courseCode=c["code"],
                        credits=c["credits"],
                        core=c["category"] == "core",
                        days=section["days"],
                        startMinute=minute(section["start_local"]),
                        endMinute=minute(section["end_local"]),
                    )
                )
        else:
            options = [
                dict(
                    id=f"tentative:{c['code']}",
                    courseCode=c["code"],
                    credits=c["credits"],
                    core=c["category"] == "core",
                    days=[],
                )
                for c in courses
                if kind in c["offered_terms"]
            ]
        terms.append(
            dict(
                code=code,
                published=is_published,
                options=sorted(options, key=lambda o: o["id"]),
            )
        )
    return DegreeRequest(
        catalogVersion=program["catalog_version"],
        courses=[
            dict(
                code=c["code"],
                credits=c["credits"],
                category=c["category"],
                prerequisiteGroups=c["prerequisite_groups"],
            )
            for c in courses
        ],
        earned=sorted(earned),
        passed=passed,
        requiredCore=program["core_course_codes"],
        categoryMinimums=dict(
            core=sum(c["credits"] for c in courses if c["category"] == "core"),
            cs_elective=program["min_cs_elective_credits"],
            gen_ed=program["min_gen_ed_credits"],
            free_elective=program["min_free_elective_credits"],
        ),
        minTotalCredits=program["min_total_credits"],
        terms=terms,
        minCredits=12,
        maxCredits=maximum,
        maxTimeSeconds=2.0,
    )


def trap(horizon):
    courses = [
        dict(code="A001", credits=3, category="core", prerequisiteGroups=[]),
        dict(code="B200", credits=3, category="core", prerequisiteGroups=[["A001"]]),
        dict(code="Z999", credits=3, category="core", prerequisiteGroups=[]),
    ]
    terms = [
        dict(
            code=f"{2027 + t // 2}-{'FALL' if t % 2 else 'SPRING'}",
            published=False,
            options=[
                dict(
                    id=f"tentative:{c['code']}",
                    courseCode=c["code"],
                    credits=3,
                    core=True,
                    days=[],
                )
                for c in courses
                if c["code"] != "Z999" or t % 2
            ],
        )
        for t in range(horizon)
    ]
    return DegreeRequest(
        catalogVersion="SYNTHETIC-TRAP",
        courses=courses,
        earned=[],
        passed=[],
        requiredCore=[c["code"] for c in courses],
        categoryMinimums=dict(core=9, cs_elective=0, gen_ed=0, free_elective=0),
        minTotalCredits=9,
        terms=terms,
        minCredits=3,
        maxCredits=3,
        maxTimeSeconds=2.0,
    )


def cases():
    result = []
    for student in ("student-ava", "student-noah"):
        for start in ("2027-SPRING", "2027-FALL"):
            for maximum, horizon in ((15, 10), (18, 8), (18, 6)):
                result.append(
                    dict(
                        id=f"{student}-{start}-{horizon}-{maximum}",
                        family="fixture",
                        request=fixture_case(student, start, horizon, maximum),
                    )
                )
        for horizon in (1, 2):
            result.append(
                dict(
                    id=f"{student}-insufficient-horizon-{horizon}",
                    family="infeasible-control",
                    request=fixture_case(student, "2027-SPRING", horizon, 18),
                )
            )
    result.append(
        dict(
            id="ava-repeat-deduplication",
            family="fixture",
            request=fixture_case("student-ava", "2027-SPRING", 8, 18, True),
        )
    )
    for horizon in (3, 4):
        result.append(
            dict(
                id=f"deadline-trap-{horizon}",
                family="designed-trap",
                request=trap(horizon),
            )
        )
    done = trap(3)
    done = done.model_copy(update={"earned": list(done.requiredCore), "passed": list(done.requiredCore)})
    result.append(dict(id="already-complete", family="complete-control", request=done))
    result.append(
        dict(
            id="zero-budget-timeout",
            family="timeout-control",
            request=trap(4).model_copy(update={"maxTimeSeconds": 0.0}),
        )
    )
    # A prior P earns elective credit, but must be retaken for a letter-grade prerequisite.
    # The repeat consumes load without earning the same degree credits twice.
    p_request = DegreeRequest(
        catalogVersion="SYNTHETIC-P-REPEAT",
        courses=[
            {"code": "R100", "credits": 3, "category": "cs_elective", "prerequisiteGroups": []},
            {"code": "C200", "credits": 3, "category": "core", "prerequisiteGroups": [["R100"]]},
        ],
        earned=["R100"],
        passed=[],
        requiredCore=["C200"],
        categoryMinimums={"core": 3, "cs_elective": 3, "gen_ed": 0, "free_elective": 0},
        minTotalCredits=6,
        terms=[
            {
                "code": f"2027-{kind}",
                "published": False,
                "options": [
                    {
                        "id": f"tentative:{code}",
                        "courseCode": code,
                        "credits": 3,
                        "core": code == "C200",
                        "days": [],
                    }
                    for code in ("R100", "C200")
                ],
            }
            for kind in ("SPRING", "FALL")
        ],
        minCredits=3,
        maxCredits=3,
        maxTimeSeconds=2.0,
    )
    result.append({"id": "approved-P-repeat", "family": "repeat-control", "request": p_request})
    return result


def run(output, repetitions):
    output.mkdir(parents=True, exist_ok=True)
    suite = cases()
    inputs = [{**c, "request": c["request"].model_dump()} for c in suite]
    (output / "cases.json").write_text(json.dumps(inputs, indent=2) + "\n")
    rows, traces = [], []
    solve_degree(trap(4))  # Explicit untimed warm-up; no benchmark case omitted from measurements.
    for case in suite:
        for repetition in range(repetitions):
            for name, method in (("cp_sat", solve_degree), ("greedy", greedy_degree)):
                started = time.perf_counter_ns()
                reply = method(case["request"])
                elapsed = (time.perf_counter_ns() - started) / 1_000_000
                errors = validate_degree(case["request"], reply)
                if errors:
                    raise RuntimeError(f"Invariant failure: {case['id']} {name}: {errors}")
                if (
                    case["family"] == "infeasible-control"
                    and name == "cp_sat"
                    and reply.status != "INFEASIBLE"
                ):
                    raise RuntimeError("Known infeasibility control did not prove infeasible")
                if case["id"] == "deadline-trap-4" and reply.completionTerms != (
                    3 if name == "cp_sat" else 4
                ):
                    raise RuntimeError("Known objective control changed")
                if case["family"] == "timeout-control" and name == "cp_sat" and reply.status != "TIMEOUT":
                    raise RuntimeError("Timeout control changed")
                credits = {c.code: c.credits for c in case["request"].courses}
                options = {(t.code, o.id): o for t in case["request"].terms for o in t.options}
                attempted = sum(
                    credits[options[s.termCode, i].courseCode]
                    for s in reply.selections
                    for i in s.selectionIds
                )
                rows.append(
                    dict(
                        case_id=case["id"],
                        family=case["family"],
                        algorithm=name,
                        repetition=repetition,
                        status=reply.status,
                        completion_terms=reply.completionTerms,
                        attempted_credits=attempted,
                        optimal=reply.optimal,
                        completion_lower_bound=reply.completionLowerBound,
                        elapsed_ms=round(elapsed, 6),
                        invariant_violations=len(errors),
                        input_sha256=digest(case["request"].model_dump()),
                    )
                )
                traces.append(
                    dict(
                        case_id=case["id"],
                        algorithm=name,
                        repetition=repetition,
                        result=reply.model_dump(),
                    )
                )
    with (output / "results.csv").open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    (output / "traces.json").write_text(json.dumps(traces, indent=2) + "\n")
    summaries = {}
    for name in ("cp_sat", "greedy"):
        own = [r for r in rows if r["algorithm"] == name]
        summaries[name] = dict(
            runs=len(own),
            feasible=sum(r["status"] == "FEASIBLE" for r in own),
            statuses={
                status: sum(r["status"] == status for r in own)
                for status in sorted({r["status"] for r in own})
            },
            median_ms=statistics.median(r["elapsed_ms"] for r in own),
            max_ms=max(r["elapsed_ms"] for r in own),
        )
    manifest = dict(
        synthetic_only=True,
        generated_at=datetime.now(UTC).isoformat(),
        cases=len(suite),
        repetitions=repetitions,
        dataset_sha256=digest(inputs),
        source_sha256={
            name: hashlib.sha256((ROOT / name).read_bytes()).hexdigest()
            for name in (
                "scripts/benchmark_degree.py",
                "services/ai/app/degree_models.py",
                "services/ai/app/degree_solver.py",
                "services/ai/app/degree_validator.py",
                "data/program.json",
                "data/courses.json",
                "data/students.json",
                "data/terms.json",
                "data/sections.json",
            )
        },
        python=platform.python_version(),
        ortools=ortools.__version__,
        os=platform.system(),
        machine=platform.machine(),
        processor=platform.processor(),
        logical_cpus=os.cpu_count(),
        solver_seed=0,
        solver_workers=1,
        solve_bound_seconds=2,
        timing="perf_counter_ns; model construction and solving included; validation and IO excluded; one untimed warmup",
        summary=summaries,
        invariant_violations=0,
        warning="Invented curriculum and transcripts. This dataset is not evidence about real graduation outcomes.",
    )
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "benchmarks/degree_planning")
    parser.add_argument("--repetitions", type=int, default=3)
    args = parser.parse_args()
    if not 1 <= args.repetitions <= 20:
        parser.error("Repetitions must be between 1 and 20")
    run(args.output, args.repetitions)
