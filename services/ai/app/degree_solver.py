"""Finite-horizon degree completion; no ML, records, identities or LLM calls."""

import math
from itertools import combinations

from ortools.sat.python import cp_model

from .degree_models import DegreeReply, DegreeRequest, DegreeSelection
from .solver import decode_status, overlaps


def complete(request: DegreeRequest, passed: set[str], earned: set[str]) -> bool:
    courses = {c.code: c for c in request.courses}
    return (
        set(request.requiredCore) <= passed
        and sum(courses[c].credits for c in earned) >= request.minTotalCredits
        and all(
            sum(courses[c].credits for c in earned if courses[c].category == category) >= minimum
            for category, minimum in request.categoryMinimums.items()
        )
    )


def solve_degree(request: DegreeRequest) -> DegreeReply:
    if complete(request, set(request.passed), set(request.earned)):
        return DegreeReply(status="FEASIBLE", optimal=True, completionTerms=0, completionLowerBound=0)
    model = cp_model.CpModel()
    horizon = len(request.terms)
    courses = {c.code: c for c in request.courses}
    x = {(c, t): model.new_bool_var(f"{c}_{t}") for c in courses for t in range(horizon)}
    y = {}
    finish = model.new_int_var(0, horizon, "completion_term")
    loads = []
    for t, term in enumerate(request.terms):
        for o in term.options:
            y[t, o.id] = model.new_bool_var(f"option_{t}_{o.id}")
        for code, course in courses.items():
            model.add(x[code, t] == sum(y[t, o.id] for o in term.options if o.courseCode == code))
            model.add(finish >= t + 1).only_enforce_if(x[code, t])
            for group in course.prerequisiteGroups:
                if not set(group) & set(request.passed):
                    model.add(sum(x[p, earlier] for p in group for earlier in range(t)) >= x[code, t])
        for a, b in combinations(term.options, 2):
            if overlaps(a, b):
                model.add(y[t, a.id] + y[t, b.id] <= 1)
        load = sum(c.credits * x[c.code, t] for c in request.courses)
        active = model.new_bool_var(f"active_{t}")
        model.add(load >= request.minCredits * active)
        model.add(load <= request.maxCredits * active)
        loads.append(load)
    for code in courses:
        model.add(sum(x[code, t] for t in range(horizon)) <= (0 if code in request.passed else 1))
        if code in request.requiredCore and code not in request.passed:
            model.add(sum(x[code, t] for t in range(horizon)) == 1)
    for category, minimum in request.categoryMinimums.items():
        initial = sum(
            c.credits for c in request.courses if c.code in request.earned and c.category == category
        )
        model.add(
            initial
            + sum(
                c.credits * x[c.code, t]
                for c in request.courses
                if c.category == category and c.code not in request.earned
                for t in range(horizon)
            )
            >= minimum
        )
    initial_total = sum(c.credits for c in request.courses if c.code in request.earned)
    model.add(
        initial_total
        + sum(
            c.credits * x[c.code, t]
            for c in request.courses
            if c.code not in request.earned
            for t in range(horizon)
        )
        >= request.minTotalCredits
    )
    # Lexicographic: completion term first, attempted credits second. No extra quality claims.
    weight = horizon * request.maxCredits + 1
    model.minimize(weight * finish + sum(loads))
    solver = cp_model.CpSolver()
    solver.parameters.max_time_in_seconds = request.maxTimeSeconds
    solver.parameters.num_search_workers = 1
    solver.parameters.random_seed = 0
    status = solver.solve(model)
    decoded = decode_status(status)
    if decoded != "FEASIBLE":
        return DegreeReply(status=decoded)
    return DegreeReply(
        status="FEASIBLE",
        optimal=status == cp_model.OPTIMAL,
        completionTerms=solver.value(finish),
        completionLowerBound=math.floor(solver.best_objective_bound / weight),
        selections=[
            DegreeSelection(
                termCode=term.code, selectionIds=[o.id for o in term.options if solver.value(y[t, o.id])]
            )
            for t, term in enumerate(request.terms)
            if t < solver.value(finish)
        ],
    )


def greedy_degree(request: DegreeRequest) -> DegreeReply:
    """Core-first, descendant-count priority, largest credits, code/ID tie breaks; no backtracking."""
    courses = {c.code: c for c in request.courses}
    passed, earned = set(request.passed), set(request.earned)
    selected = []
    descendants = {
        c: {d.code for d in request.courses if any(c in g for g in d.prerequisiteGroups)} for c in courses
    }
    for _ in courses:
        for c in courses:
            descendants[c] |= set().union(*(descendants[d] for d in list(descendants[c])))
    for t, term in enumerate(request.terms):
        if complete(request, passed, earned):
            return DegreeReply(status="FEASIBLE", selections=selected, completionTerms=t)
        chosen, codes, load = [], set(), 0
        options = sorted(
            term.options,
            key=lambda o: (
                courses[o.courseCode].category != "core",
                -len(descendants[o.courseCode]),
                -o.credits,
                o.courseCode,
                o.id,
            ),
        )
        for o in options:
            c = courses[o.courseCode]
            if c.code in passed or c.code in codes or any(not set(g) & passed for g in c.prerequisiteGroups):
                continue
            category_earned = sum(courses[e].credits for e in earned if courses[e].category == c.category)
            if (
                c.category != "core"
                and category_earned >= request.categoryMinimums[c.category]
                and complete(request, passed | codes, earned | codes)
            ):
                continue
            if load + c.credits <= request.maxCredits and all(not overlaps(o, other) for other in chosen):
                chosen.append(o)
                codes.add(c.code)
                load += c.credits
        if load < request.minCredits:
            chosen, codes = [], set()  # Wait for another offering; never silently violate the minimum.
        selected.append(DegreeSelection(termCode=term.code, selectionIds=[o.id for o in chosen]))
        passed |= codes
        earned |= codes
    if complete(request, passed, earned):
        return DegreeReply(status="FEASIBLE", selections=selected, completionTerms=len(request.terms))
    return DegreeReply(status="NO_SOLUTION")  # Heuristic failure is never a proof of infeasibility.
