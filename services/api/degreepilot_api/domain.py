"""Deterministic academic rules, independent of transport, persistence, and the solver."""

import re
from dataclasses import dataclass
from datetime import datetime
from decimal import ROUND_HALF_UP, Decimal
from itertools import combinations
from zoneinfo import ZoneInfo

from .schemas import Attempt, Course, PlanRequest, Program, Section, Term

CATEGORIES = ("core", "cs_elective", "gen_ed", "free_elective")
GRADE_POINTS = {"A": 4, "B": 3, "C": 2, "D": 1, "F": 0}
WEEKDAYS = {"Mon", "Tue", "Wed", "Thu", "Fri"}
TERM_PATTERN = re.compile(r"[0-9]{4}-(SPRING|FALL)\Z")


def now() -> datetime:
    return datetime.now(ZoneInfo("America/New_York"))


def issue(code: str, message: str) -> dict:
    return {"code": code, "message": message}


def term_order(code: str) -> int:
    return int(code[:4]) * 2 + int(code.endswith("FALL"))


def catalog_issues(program: Program | None, courses: list[Course], version: str) -> list[dict]:
    errors = []

    def invalid(message: str):
        errors.append(issue("CATALOG_INVALID", message))

    if program is None or program.catalog_version != version:
        return [issue("CATALOG_INVALID", "The student's pinned catalog is missing or mismatched.")]
    if (
        not all(
            (
                program.institution.strip(),
                program.program_id.strip(),
                program.program_name.strip(),
                program.grade_policy_document.strip(),
            )
        )
        or not 1900 <= program.effective_year <= 9999
        or program.min_total_credits <= 0
        or min(program.min_cs_elective_credits, program.min_gen_ed_credits, program.min_free_elective_credits)
        < 0
        or program.min_passing_grade != "C"
        or program.grade_point_scale != GRADE_POINTS
        or any(c not in CATEGORIES or c == "core" for c in program.pass_credit_categories)
    ):
        invalid("Required credit or grade rules are missing or unsupported.")
    core = program.core_course_codes
    if not core or any(not c.strip() for c in core) or len(core) != len(set(core)):
        invalid("Core requirements must be a nonempty set of unique course codes.")
        return errors
    by_code = {c.code: c for c in courses}
    if not courses or len(by_code) != len(courses) or any(not c.code.strip() for c in courses):
        invalid("The catalog contains missing or duplicate course codes.")
        return errors
    for code in core:
        if code not in by_code or by_code[code].category != "core":
            invalid(f"Required core course {code} is missing or has a conflicting category.")
    for course in courses:
        if (
            course.catalog_version != version
            or not 1 <= course.credits <= 30
            or not course.title.strip()
            or course.category not in CATEGORIES
            or course.category == "core"
            and course.code not in core
        ):
            invalid(f"Course {course.code} has invalid credits, category, or catalog version.")
        if any(t not in {"Fall", "Spring"} for t in course.offered_terms):
            invalid(f"Course {course.code} has unsupported offering data.")
        if any(
            not group or any(code not in by_code for code in group) for group in course.prerequisite_groups
        ):
            invalid(f"Course {course.code} references missing or malformed prerequisites.")
    if errors:
        return errors
    minimums = {
        "cs_elective": program.min_cs_elective_credits,
        "gen_ed": program.min_gen_ed_credits,
        "free_elective": program.min_free_elective_credits,
    }
    if program.min_total_credits > sum(c.credits for c in courses) or any(
        minimum > sum(c.credits for c in courses if c.category == category)
        for category, minimum in minimums.items()
    ):
        invalid("The catalog cannot supply its required total or category credits.")
    visiting, visited = set(), set()

    def cycle(code: str) -> bool:
        if code in visiting:
            return True
        if code in visited:
            return False
        visiting.add(code)
        for group in by_code[code].prerequisite_groups:
            if any(cycle(dependency) for dependency in group):
                return True
        visiting.remove(code)
        visited.add(code)
        return False

    if any(cycle(c.code) for c in courses):
        invalid("The catalog contains a prerequisite cycle.")
    return errors


def audit(program: Program | None, courses: list[Course], attempts: list[Attempt], version: str) -> dict:
    errors = catalog_issues(program, courses, version)
    result = dict(
        status="invalid",
        catalogVersion=version,
        ruleEngineVersion="2.0.0",
        generatedAt=now().isoformat(),
        earnedCredits=None,
        requiredCredits=None,
        gpa=None,
        degreeComplete=None,
        buckets=[],
        coreRequirements=[],
        earnedCourses=[],
        issues=errors,
    )
    if errors or program is None:
        return result
    by_code = {c.code: c for c in courses}
    if len({(a.course_code, a.term) for a in attempts}) != len(attempts):
        errors.append(
            issue("TRANSCRIPT_INVALID", "Duplicate course attempts in the same term are unsupported.")
        )
    for attempt in attempts:
        if attempt.course_code not in by_code:
            errors.append(
                issue(
                    "TRANSCRIPT_INVALID",
                    "A course is not in the pinned catalog; transfer equivalency is unsupported.",
                )
            )
        if not TERM_PATTERN.fullmatch(attempt.term):
            errors.append(issue("TRANSCRIPT_INVALID", "An attempt has an invalid term."))
        if attempt.grade not in {*GRADE_POINTS, "W", "I", "P"}:
            errors.append(issue("TRANSCRIPT_INVALID", "An attempt has an unsupported grade."))
    if errors:
        return result
    earned = {}
    for attempt in sorted(attempts, key=lambda a: term_order(a.term)):
        course = by_code[attempt.course_code]
        if attempt.grade in {"A", "B", "C"} or (
            attempt.grade == "P" and course.category in program.pass_credit_categories
        ):
            earned[course.code] = dict(
                code=course.code,
                credits=course.credits,
                category=course.category,
                grade=attempt.grade,
                term=attempt.term,
            )
    core = [
        dict(
            code=code,
            title=by_code[code].title,
            credits=by_code[code].credits,
            complete=code in earned,
            grade=earned.get(code, {}).get("grade"),
            term=earned.get(code, {}).get("term"),
        )
        for code in program.core_course_codes
    ]
    minimums = [
        sum(c["credits"] for c in core),
        program.min_cs_elective_credits,
        program.min_gen_ed_credits,
        program.min_free_elective_credits,
    ]
    buckets = []
    for category, label, required in zip(
        CATEGORIES, ("Required core", "CS electives", "General education", "Free electives"), minimums
    ):
        credits = sum(c["credits"] for c in earned.values() if c["category"] == category)
        buckets.append(
            dict(
                id=category,
                label=label,
                earned=credits,
                required=required,
                remaining=max(0, required - credits),
                complete=credits >= required,
            )
        )
    graded = [a for a in attempts if a.grade in GRADE_POINTS]
    gpa_credits = sum(by_code[a.course_code].credits for a in graded)
    gpa = (
        None
        if not gpa_credits
        else float(
            (
                Decimal(sum(GRADE_POINTS[a.grade] * by_code[a.course_code].credits for a in graded))
                / gpa_credits
            ).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)
        )
    )
    total = sum(c["credits"] for c in earned.values())
    result.update(
        status="valid",
        earnedCredits=total,
        requiredCredits=program.min_total_credits,
        gpa=gpa,
        degreeComplete=total >= program.min_total_credits
        and all(b["complete"] for b in buckets)
        and all(c["complete"] for c in core),
        buckets=buckets,
        coreRequirements=core,
        earnedCourses=[earned[code] for code in sorted(earned)],
    )
    return result


@dataclass(frozen=True)
class Context:
    version: str
    program: Program | None
    courses: list[Course]
    attempts: list[Attempt]
    term: Term
    sections: list[Section]


def valid_term(term: Term) -> bool:
    return bool(TERM_PATTERN.fullmatch(term.code)) and (
        term.kind == ("Fall" if term.code.endswith("FALL") else "Spring")
        and term.starts_on < term.ends_on
        and term.starts_on.year == int(term.code[:4])
        and term.timezone == "America/New_York"
    )


def minute(clock: str) -> int:
    if not re.fullmatch(r"(?:[01][0-9]|2[0-3]):[0-5][0-9]", clock):
        raise ValueError("Invalid local meeting time")
    return int(clock[:2]) * 60 + int(clock[3:])


def valid_section(section: Section, term: Term) -> bool:
    try:
        return (
            section.term_code == term.code
            and section.timezone == term.timezone
            and bool(section.days)
            and len(set(section.days)) == len(section.days)
            and set(section.days) <= WEEKDAYS
            and section.capacity > 0
            and 0 <= section.available_seats <= section.capacity
            and minute(section.start_local) < minute(section.end_local)
        )
    except ValueError:
        return False


def context_issues(context: Context) -> list[dict]:
    errors = audit(context.program, context.courses, context.attempts, context.version)["issues"]
    if errors:
        return errors
    if (
        not valid_term(context.term)
        or len({s.id for s in context.sections}) != len(context.sections)
        or any(
            s.catalog_version != context.version
            or s.course_code not in {c.code for c in context.courses}
            or not valid_section(s, context.term)
            for s in context.sections
        )
    ):
        return [issue("CATALOG_INVALID", "Term or section data is invalid for this catalog.")]
    return []


def eligibility(context: Context, code: str) -> dict:
    errors = context_issues(context)
    course = next((c for c in context.courses if c.code == code), None)
    result = dict(
        courseCode=code,
        status="invalid",
        eligible=False,
        tentative=not context.term.is_published,
        missingPrerequisiteGroups=[],
        reasons=errors,
    )
    if errors:
        return result
    if course is None:
        result["reasons"] = [issue("CATALOG_INVALID", "The course is not in the student's pinned catalog.")]
        return result
    completed = {
        a.course_code
        for a in context.attempts
        if a.grade in {"A", "B", "C"} and term_order(a.term) < term_order(context.term.code)
    }
    if code in completed:
        result.update(
            status="completed",
            reasons=[issue("ALREADY_COMPLETED", "Completed with C or better before this term.")],
        )
        return result
    missing = [g for g in course.prerequisite_groups if not set(g) & completed]
    sections = [s for s in context.sections if s.course_code == code]
    offered = bool(sections) if context.term.is_published else context.term.kind in course.offered_terms
    full = context.term.is_published and bool(sections) and all(s.available_seats == 0 for s in sections)
    reasons = [
        issue(
            "MISSING_PREREQUISITE", f"Complete {' or '.join(g)} with C or better before {context.term.code}."
        )
        for g in missing
    ]
    if not offered:
        reasons.append(
            issue(
                "NOT_OFFERED",
                "No published section exists for this term."
                if context.term.is_published
                else "The catalog does not advertise this term pattern.",
            )
        )
    if full:
        reasons.append(issue("SECTION_FULL", "All published sections have no available seats."))
    result.update(
        status="blocked" if missing or full else "not_offered" if not offered else "eligible",
        eligible=offered and not full and not missing,
        missingPrerequisiteGroups=missing,
        reasons=reasons,
    )
    return result


def option(course: Course, term: Term, section: Section | None = None) -> dict:
    return dict(
        id=section.id if section else f"tentative:{course.code}",
        courseCode=course.code,
        title=course.title,
        credits=course.credits,
        category=course.category,
        sectionCode=section.section_code if section else None,
        days=section.days if section else [],
        startLocal=section.start_local if section else None,
        endLocal=section.end_local if section else None,
        timezone=term.timezone,
        availableSeats=section.available_seats if section else None,
        tentative=not term.is_published,
    )


def options(context: Context) -> list[dict]:
    eligible = {c.code: c for c in context.courses if eligibility(context, c.code)["eligible"]}
    if not context.term.is_published:
        return [option(eligible[code], context.term) for code in sorted(eligible)]
    return [
        option(eligible[s.course_code], context.term, s)
        for s in sorted(context.sections, key=lambda s: s.id)
        if s.course_code in eligible and s.available_seats > 0
    ]


def plan_result(context: Context, status: str, errors: list[dict], selections=None) -> dict:
    selections = selections or []
    return dict(
        status=status,
        catalogVersion=context.version,
        termCode=context.term.code,
        tentative=not context.term.is_published,
        totalCredits=sum(o["credits"] for o in selections),
        selections=selections,
        issues=errors,
        id=None,
    )


def check_request(context: Context, request: PlanRequest, require_future: bool = True) -> list[dict]:
    errors = context_issues(context)
    if request.term_code != context.term.code:
        errors.append(issue("TERM_MISMATCH", "The plan term does not match its data."))
    if require_future and context.term.starts_on <= now().date():
        errors.append(issue("TERM_NOT_FUTURE", "Choose a future term for planning."))
    return errors


def validate_plan(
    context: Context, request: PlanRequest, ids: list[str], require_future: bool = True
) -> dict:
    errors = check_request(context, request, require_future)
    if errors:
        return plan_result(context, "INVALID", errors)
    if not ids or len(ids) > 30 or len(ids) != len(set(ids)):
        errors.append(
            issue(
                "DUPLICATE_OR_EMPTY_SELECTION", "Select unique courses or sections; a plan cannot be empty."
            )
        )
    selected = []
    by_code = {c.code: c for c in context.courses}
    by_id = {s.id: s for s in context.sections}
    for selection in ids[:30]:
        section = by_id.get(selection) if context.term.is_published else None
        course = (
            by_code.get(section.course_code)
            if section
            else next((c for c in context.courses if f"tentative:{c.code}" == selection), None)
            if not context.term.is_published
            else None
        )
        if course is None:
            errors.append(
                issue(
                    "SECTION_INVALID" if context.term.is_published else "COURSE_INVALID",
                    "The selection does not belong to this term and catalog.",
                )
            )
            continue
        errors.extend(eligibility(context, course.code)["reasons"])
        if section and section.available_seats <= 0:
            errors.append(issue("SECTION_FULL", f"{section.section_code} has no available seats."))
        selected.append(option(course, context.term, section))
    if len({s["courseCode"] for s in selected}) != len(selected):
        errors.append(issue("DUPLICATE_COURSE", "Select at most one section of each course."))
    if context.term.is_published:
        for a, b in combinations([s for s in context.sections if s.id in ids], 2):
            if (
                set(a.days) & set(b.days)
                and minute(a.start_local) < minute(b.end_local)
                and minute(b.start_local) < minute(a.end_local)
            ):
                errors.append(issue("TIME_CONFLICT", f"{a.section_code} overlaps {b.section_code}."))
    credits = sum(s["credits"] for s in selected)
    if not request.min_credits <= credits <= request.max_credits:
        errors.append(
            issue(
                "CREDIT_RANGE",
                f"Selected {credits} credits; the requested range is {request.min_credits}–{request.max_credits}.",
            )
        )
    return plan_result(context, "INVALID" if errors else "FEASIBLE", errors, selected)
