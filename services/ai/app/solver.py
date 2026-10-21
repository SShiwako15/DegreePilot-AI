from itertools import combinations

from ortools.sat.python import cp_model

from .models import Option, SolveRequest, SolveResult


def overlaps(a: Option, b: Option) -> bool:
    # Deliberately independent of the ASP.NET interval validator.
    return bool(set(a.days) & set(b.days)) and (a.startMinute < b.endMinute and b.startMinute < a.endMinute)


def decode_status(status: int) -> str:
    if status in (cp_model.OPTIMAL, cp_model.FEASIBLE):
        return "FEASIBLE"
    if status == cp_model.INFEASIBLE:
        return "INFEASIBLE"
    if status == cp_model.UNKNOWN:
        return "TIMEOUT"
    return "ERROR"


def solve(request: SolveRequest) -> SolveResult:
    model = cp_model.CpModel()
    choices = {o.id: model.new_bool_var(o.id) for o in request.options}
    for a, b in combinations(request.options, 2):
        if a.courseCode == b.courseCode or overlaps(a, b):
            model.add(choices[a.id] + choices[b.id] <= 1)
    credits = sum(o.credits * choices[o.id] for o in request.options)
    model.add(credits >= request.minCredits)
    model.add(credits <= request.maxCredits)

    day_vars = []
    for day in ("Mon", "Tue", "Wed", "Thu", "Fri"):
        meetings = [choices[o.id] for o in request.options if day in o.days]
        if meetings:
            used = model.new_bool_var(f"day_{day}")
            model.add_max_equality(used, meetings)
            day_vars.append(used)
    # Preferences are soft objectives; never ban an otherwise valid weekday.
    objective = sum(
        (
            o.credits * (100 if o.core else 10)
            - (5 * len(set(o.days) - set(request.preferredDays)) if request.preferredDays else 0)
        )
        * choices[o.id]
        for o in request.options
    )
    if request.fewerCampusDays:
        objective -= sum(day_vars)
    model.maximize(objective)
    solver = cp_model.CpSolver()
    solver.parameters.max_time_in_seconds = request.maxTimeSeconds
    solver.parameters.num_search_workers = 1
    solver.parameters.random_seed = 0
    status = solver.solve(model)
    result_status = decode_status(status)
    selected = (
        [o.id for o in request.options if solver.value(choices[o.id])] if result_status == "FEASIBLE" else []
    )
    return SolveResult(status=result_status, selectionIds=selected)
