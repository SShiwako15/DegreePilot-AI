"""Independent replay checker shared by benchmarks, not CP-SAT constraint construction."""

from itertools import combinations

from .degree_models import DegreeReply, DegreeRequest


def validate_degree(request: DegreeRequest, reply: DegreeReply) -> list[str]:
    errors = []
    if reply.status != "FEASIBLE":
        return ["NONFEASIBLE_HAS_SELECTIONS"] if reply.selections else []
    courses = {c.code: c for c in request.courses}
    passed, earned, scheduled = set(request.passed), set(request.earned), set()
    term_rows = {row.termCode: row for row in reply.selections}
    if len(term_rows) != len(reply.selections) or any(
        code not in {t.code for t in request.terms} for code in term_rows
    ):
        errors.append("TERM_INVALID")
    finish = 0
    for index, term in enumerate(request.terms):
        row = term_rows.get(term.code)
        ids = row.selectionIds if row else []
        options = {o.id: o for o in term.options}
        if len(set(ids)) != len(ids) or any(i not in options for i in ids):
            errors.append("SELECTION_INVALID")
            continue
        choices = [options[i] for i in ids]
        codes = [o.courseCode for o in choices]
        if len(set(codes)) != len(codes) or any(c in scheduled or c in passed for c in codes):
            errors.append("DUPLICATE_COURSE")
        for c in codes:
            if any(not any(p in passed for p in group) for group in courses[c].prerequisiteGroups):
                errors.append("MISSING_PREREQUISITE")
        load = sum(courses[c].credits for c in codes)
        if choices and not request.minCredits <= load <= request.maxCredits:
            errors.append("CREDIT_RANGE")
        for a, b in combinations(choices, 2):
            if (
                set(a.days).intersection(b.days)
                and a.startMinute < b.endMinute
                and b.startMinute < a.endMinute
            ):
                errors.append("TIME_CONFLICT")
        if choices:
            finish = index + 1
        scheduled.update(codes)
        earned.update(codes)
        passed.update(codes)  # Conditional future C passes, not changes to actual records.
    if not set(request.requiredCore) <= passed:
        errors.append("CORE_INCOMPLETE")
    if sum(courses[c].credits for c in earned) < request.minTotalCredits:
        errors.append("TOTAL_INCOMPLETE")
    for category, minimum in request.categoryMinimums.items():
        if sum(courses[c].credits for c in earned if courses[c].category == category) < minimum:
            errors.append("BUCKET_INCOMPLETE")
    if reply.completionTerms != finish:
        errors.append("COMPLETION_MISMATCH")
    return errors
