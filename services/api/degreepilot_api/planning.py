import json
from dataclasses import replace
from datetime import UTC, date, datetime
from uuid import uuid4

from sqlalchemy import select

from . import models as m
from .database import academic, add_audit, load_context, section_model, term_model
from .domain import (
    Context,
    audit,
    check_request,
    context_issues,
    issue,
    minute,
    now,
    options,
    plan_result,
    term_order,
    validate_plan,
)
from .policies import canonical, digest
from .schemas import Attempt, DegreeRequest, PlanRequest, Term


def solver_option(option):
    return dict(
        id=option["id"],
        courseCode=option["courseCode"],
        credits=option["credits"],
        core=option["category"] == "core",
        days=option["days"],
        startMinute=minute(option["startLocal"]) if option["startLocal"] else None,
        endMinute=minute(option["endLocal"]) if option["endLocal"] else None,
    )


async def generate(db, actor, context, request, gateway):
    errors = check_request(context, request)
    if errors:
        return plan_result(context, "INVALID", errors)
    candidates = options(context)
    available = sum({o["courseCode"]: o["credits"] for o in candidates}.values())
    if available < request.min_credits:
        return plan_result(
            context,
            "INFEASIBLE",
            [
                issue(
                    "INSUFFICIENT_ELIGIBLE_CREDITS",
                    f"Only {available} distinct eligible credits have available offerings; requested at least {request.min_credits}.",
                )
            ],
        )
    problem = dict(
        options=[solver_option(o) for o in candidates],
        minCredits=request.min_credits,
        maxCredits=request.max_credits,
        preferredDays=request.preferred_days or [],
        fewerCampusDays=request.fewer_campus_days,
        maxTimeSeconds=5.0,
    )
    # End the read snapshot while the remote solver runs; re-read authoritative data afterwards.
    db.rollback()
    reply = await gateway.call("solve", problem)
    if reply is None:
        return plan_result(
            context,
            "UNAVAILABLE",
            [
                issue(
                    "PLANNER_UNAVAILABLE",
                    "The solver service is unavailable. Manual validation remains available.",
                )
            ],
        )
    ids = reply.get("selectionIds")
    if not isinstance(ids, list) or len(ids) > 30 or any(not isinstance(i, str) for i in ids):
        return plan_result(
            context, "ERROR", [issue("SOLVER_RESULT_INVALID", "The solver returned invalid selections.")]
        )
    if reply.get("status") == "FEASIBLE":
        current = load_context(db, actor, request.term_code)
        if current is None or current.version != context.version:
            return plan_result(
                context,
                "ERROR",
                [issue("PLANNING_DATA_CHANGED", "Planning data changed. Refresh and retry.")],
            )
        validated = validate_plan(current, request, ids)
        if validated["status"] == "FEASIBLE":
            return validated
        return plan_result(
            context,
            "ERROR",
            [
                issue("SOLVER_RESULT_INVALID", "The backend rejected an invalid solver proposal."),
                *validated["issues"],
            ],
        )
    if ids:
        return plan_result(
            context,
            "ERROR",
            [issue("SOLVER_RESULT_INVALID", "Non-feasible solver results cannot contain selections.")],
        )
    status = reply.get("status")
    code, message = {
        "INFEASIBLE": (
            "NO_VALID_COMBINATION",
            "No combination satisfies credit, course, seat and meeting constraints.",
        ),
        "TIMEOUT": (
            "SOLVER_TIMEOUT",
            "The time limit elapsed without a feasible proposal. This does not prove infeasibility.",
        ),
    }.get(status, ("SOLVER_ERROR", "No trusted result was returned. Manual validation remains available."))
    return plan_result(
        context, status if status in {"INFEASIBLE", "TIMEOUT"} else "ERROR", [issue(code, message)]
    )


def save(db, actor, request, suggestion_advisor=None):
    context = load_context(db, actor, request.constraints.term_code)
    if context is None:
        return None
    result = validate_plan(context, request.constraints, request.selection_ids)
    if result["status"] != "FEASIBLE":
        return result
    values = dict(
        id=uuid4(),
        student_id=actor,
        catalog_version=context.version,
        term_code=context.term.code,
        constraints_json=request.constraints.model_dump_json(),
        selection_ids_json=json.dumps(request.selection_ids),
        created_at=datetime.now(UTC),
    )
    if suggestion_advisor:
        row = m.Suggestion(advisor_id=suggestion_advisor, **values)
        action, author = "advisor.suggest", suggestion_advisor
    else:
        row, action, author = m.Plan(**values), "plan.save", actor
    db.add(row)
    add_audit(db, author, action, str(row.id))
    db.commit()
    result["id"] = str(row.id)
    return result


def read_plan(db, row):
    context = load_context(db, row.student_id, row.term_code)
    try:
        request = PlanRequest.model_validate(
            {key[0].lower() + key[1:]: value for key, value in json.loads(row.constraints_json).items()}
        )
        ids = json.loads(row.selection_ids_json)
        if (
            context
            and context.version == row.catalog_version
            and isinstance(ids, list)
            and all(isinstance(i, str) for i in ids)
        ):
            return {**validate_plan(context, request, ids, require_future=False), "id": str(row.id)}
    except (ValueError, TypeError):
        pass
    return dict(
        status="INVALID",
        catalogVersion=row.catalog_version,
        termCode=row.term_code,
        tentative=not context or not context.term.is_published,
        totalCredits=0,
        selections=[],
        issues=[issue("PLAN_INVALID", "Saved plan data is no longer valid for its pinned catalog.")],
        id=str(row.id),
    )


def degree_context(db, actor, request):
    data = academic(db, actor)
    if data is None:
        return None
    terms = {t.code: term_model(t) for t in db.scalars(select(m.Term))}
    sections = [
        section_model(s)
        for s in db.scalars(
            select(m.Section).where(m.Section.catalog_version == data[0]).order_by(m.Section.id)
        )
    ]
    contexts = []
    for offset in range(request.horizon_terms):
        order = term_order(request.start_term_code) + offset
        year, fall = divmod(order, 2)
        kind = "Fall" if fall else "Spring"
        code = f"{year}-{kind.upper()}"
        term = terms.get(code) or Term(
            code=code,
            name=f"{kind} {year} (tentative)",
            kind=kind,
            starts_on=date(year, 8 if fall else 1, 15),
            ends_on=date(year, 12 if fall else 5, 15),
            timezone="America/New_York",
            is_published=False,
        )
        contexts.append(
            Context(data[0], data[1], data[2], data[3], term, [s for s in sections if s.term_code == code])
        )
    return contexts


def degree_fingerprint(contexts, request):
    context = contexts[0]
    return digest(
        canonical(
            dict(
                version=context.version,
                program=context.program.model_dump() if context.program else None,
                courses=[c.model_dump() for c in context.courses],
                attempts=[
                    a.model_dump() for a in sorted(context.attempts, key=lambda a: (a.course_code, a.term))
                ],
                terms=[
                    dict(term=c.term.model_dump(mode="json"), sections=[s.model_dump() for s in c.sections])
                    for c in contexts
                ],
                request=request.model_dump(),
            )
        )
    )


def degree_problem(contexts, request, initial):
    context = contexts[0]
    program = context.program
    passed = sorted({a.course_code for a in context.attempts if a.grade in {"A", "B", "C"}})
    terms = []
    for c in contexts:
        all_options = []
        by_code = {course.code: course for course in c.courses}
        if c.term.is_published:
            from .domain import option

            all_options = [
                option(by_code[s.course_code], c.term, s) for s in c.sections if s.available_seats > 0
            ]
        else:
            from .domain import option

            all_options = [
                option(course, c.term) for course in c.courses if c.term.kind in course.offered_terms
            ]
        terms.append(
            dict(
                code=c.term.code,
                published=c.term.is_published,
                options=[solver_option(o) for o in all_options],
            )
        )
    return dict(
        catalogVersion=context.version,
        courses=[
            dict(
                code=c.code, credits=c.credits, category=c.category, prerequisiteGroups=c.prerequisite_groups
            )
            for c in context.courses
        ],
        earned=[c["code"] for c in initial["earnedCourses"]],
        passed=passed,
        requiredCore=program.core_course_codes,
        categoryMinimums={b["id"]: b["required"] for b in initial["buckets"]},
        minTotalCredits=program.min_total_credits,
        terms=terms,
        minCredits=request.min_credits,
        maxCredits=request.max_credits,
        maxTimeSeconds=5.0,
    )


def validate_degree(contexts, request, reply, fingerprint):
    base = dict(
        status="ERROR",
        catalogVersion=contexts[0].version,
        inputFingerprint=fingerprint,
        terms=[],
        issues=[],
        optimal=False,
        completionTerms=None,
        completionLowerBound=None,
        conditionalDegreeComplete=False,
        id=None,
    )

    def invalid(code, message):
        return {**base, "issues": [issue(code, message)]}

    if reply.get("status") != "FEASIBLE":
        return invalid("DEGREE_RESULT_INVALID", "Only feasible selections can be validated.")
    rows = reply.get("selections")
    if (
        not isinstance(rows, list)
        or len(rows) > len(contexts)
        or any(
            not isinstance(row, dict)
            or not isinstance(row.get("termCode"), str)
            or not isinstance(row.get("selectionIds"), list)
            or len(row["selectionIds"]) > 30
            or any(not isinstance(i, str) for i in row["selectionIds"])
            for row in rows
        )
    ):
        return invalid("DEGREE_RESULT_INVALID", "The proposal contains invalid term selections.")
    mapping = {row["termCode"]: row["selectionIds"] for row in rows}
    if len(mapping) != len(rows) or not set(mapping) <= {c.term.code for c in contexts}:
        return invalid("DEGREE_RESULT_INVALID", "The proposal contains duplicate or unknown terms.")
    projected, scheduled, plans, finish = list(contexts[0].attempts), set(), [], 0
    for index, context in enumerate(contexts):
        ids = mapping.get(context.term.code, [])
        if not ids:
            continue
        plan = validate_plan(
            replace(context, attempts=list(projected)),
            PlanRequest(
                term_code=context.term.code, min_credits=request.min_credits, max_credits=request.max_credits
            ),
            ids,
        )
        if plan["status"] != "FEASIBLE":
            return {
                **base,
                "issues": [
                    issue("DEGREE_RESULT_INVALID", "A semester failed independent validation."),
                    *plan["issues"],
                ],
            }
        for course in plan["selections"]:
            if course["courseCode"] in scheduled:
                return invalid("DUPLICATE_COURSE", "A course appears in multiple semesters.")
            scheduled.add(course["courseCode"])
            projected.append(Attempt(course_code=course["courseCode"], term=context.term.code, grade="C"))
        plans.append(plan)
        finish = index + 1
    final = audit(contexts[0].program, contexts[0].courses, projected, contexts[0].version)
    bound, optimal = reply.get("completionLowerBound"), reply.get("optimal", False)
    if (
        not final["degreeComplete"]
        or type(reply.get("completionTerms")) is not int
        or reply["completionTerms"] != finish
        or type(optimal) is not bool
        or bound is not None
        and (type(bound) is not int or not 0 <= bound <= finish)
        or optimal
        and bound != finish
    ):
        return invalid(
            "DEGREE_INCOMPLETE", "The conditional audit, completion term, or reported bound is invalid."
        )
    return {
        **base,
        "status": "FEASIBLE",
        "terms": plans,
        "optimal": optimal,
        "completionTerms": finish,
        "completionLowerBound": bound,
        "conditionalDegreeComplete": True,
    }


async def generate_degree(db, actor: str, request: DegreeRequest, gateway):
    contexts = degree_context(db, actor, request)
    if contexts is None:
        return None
    context = contexts[0]
    fingerprint = degree_fingerprint(contexts, request)

    def failure(status, code, message):
        return dict(
            status=status,
            catalogVersion=context.version,
            inputFingerprint=fingerprint,
            terms=[],
            issues=[issue(code, message)],
            optimal=False,
            completionTerms=None,
            completionLowerBound=None,
            conditionalDegreeComplete=False,
            id=None,
        )

    initial = audit(context.program, context.courses, context.attempts, context.version)
    if initial["status"] != "valid" or len(context.courses) > 200 or any(context_issues(c) for c in contexts):
        return failure(
            "INVALID",
            "CATALOG_INVALID",
            "The pinned catalog or section data cannot support a trusted roadmap.",
        )
    if (
        not now().year <= int(request.start_term_code[:4]) <= now().year + 6
        or context.term.starts_on <= now().date()
        or any(term_order(a.term) >= term_order(request.start_term_code) for a in context.attempts)
    ):
        return failure("INVALID", "TERM_INVALID", "Choose a future term after every recorded attempt.")
    problem = degree_problem(contexts, request, initial)
    if any(len(t["options"]) > 300 for t in problem["terms"]):
        return failure("INVALID", "OPTIONS_LIMIT", "The offering exceeds the bounded research model.")
    db.rollback()
    reply = await gateway.call("degree/solve", problem)
    if reply is None:
        return failure(
            "UNAVAILABLE",
            "PLANNER_UNAVAILABLE",
            "The research solver is unavailable. Manual planning remains available.",
        )
    if reply.get("status") != "FEASIBLE":
        if reply.get("selections"):
            return failure("ERROR", "DEGREE_RESULT_INVALID", "A non-feasible result contained selections.")
        status = reply.get("status")
        code, message = {
            "INFEASIBLE": (
                "HORIZON_INFEASIBLE",
                "No complete plan satisfies this finite horizon. This does not prove graduation is impossible.",
            ),
            "TIMEOUT": (
                "SOLVER_TIMEOUT",
                "No feasible roadmap was found within the bound; infeasibility is not established.",
            ),
        }.get(status, ("SOLVER_ERROR", "No trusted roadmap was returned."))
        return failure(status if status in {"INFEASIBLE", "TIMEOUT"} else "ERROR", code, message)
    current = degree_context(db, actor, request)
    if current is None or degree_fingerprint(current, request) != fingerprint:
        return failure(
            "ERROR",
            "PLANNING_DATA_CHANGED",
            "Academic records or offerings changed during solving. Refresh and retry.",
        )
    result = validate_degree(current, request, reply, fingerprint)
    if result["status"] == "FEASIBLE":
        result["id"] = str(uuid4())
        add_audit(db, actor, "degree-plan.generate", result["id"], content_hash=digest(canonical(result)))
        db.commit()
    return result
