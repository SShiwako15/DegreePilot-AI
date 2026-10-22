from typing import Literal

from pydantic import Field, model_validator

from .models import Option, StrictModel

CATEGORIES = ("core", "cs_elective", "gen_ed", "free_elective")


class DegreeCourse(StrictModel):
    code: str = Field(min_length=1, max_length=30)
    credits: int = Field(ge=1, le=30)
    category: Literal["core", "cs_elective", "gen_ed", "free_elective"]
    prerequisiteGroups: list[list[str]] = Field(max_length=30)


class DegreeTerm(StrictModel):
    code: str = Field(pattern=r"^[0-9]{4}-(SPRING|FALL)$")
    published: bool
    options: list[Option] = Field(max_length=300)


class DegreeRequest(StrictModel):
    catalogVersion: str = Field(min_length=1, max_length=100)
    courses: list[DegreeCourse] = Field(min_length=1, max_length=200)
    earned: list[str] = Field(max_length=200)
    passed: list[str] = Field(max_length=200)
    requiredCore: list[str] = Field(min_length=1, max_length=200)
    categoryMinimums: dict[str, int]
    minTotalCredits: int = Field(ge=1, le=6000)
    terms: list[DegreeTerm] = Field(min_length=1, max_length=12)
    minCredits: int = Field(ge=1, le=30)
    maxCredits: int = Field(ge=1, le=30)
    maxTimeSeconds: float = Field(default=5.0, ge=0, le=5)

    @model_validator(mode="after")
    def consistent(self):
        by_code = {c.code: c for c in self.courses}
        if len(by_code) != len(self.courses) or self.minCredits > self.maxCredits:
            raise ValueError("Duplicate courses or reversed bounds")
        for codes in (self.earned, self.passed, self.requiredCore):
            if len(set(codes)) != len(codes) or not set(codes) <= by_code.keys():
                raise ValueError("Unknown or duplicate course IDs")
        if not set(self.passed) <= set(self.earned):
            raise ValueError("Passing letter grades must earn credit")
        if set(self.requiredCore) != {c.code for c in self.courses if c.category == "core"}:
            raise ValueError("Core requirements must match categories")
        if set(self.categoryMinimums) != set(CATEGORIES) or any(
            type(n) is not int or n < 0 or n > 6000 for n in self.categoryMinimums.values()
        ):
            raise ValueError("Invalid degree buckets")
        visiting, done = set(), set()

        def visit(code):
            if code in visiting:
                raise ValueError("Prerequisite cycle")
            if code in done:
                return
            visiting.add(code)
            for group in by_code[code].prerequisiteGroups:
                if not group or len(group) > 30 or any(p not in by_code for p in group):
                    raise ValueError("Unknown or empty prerequisite")
                for p in group:
                    visit(p)
            visiting.remove(code)
            done.add(code)

        for code in by_code:
            visit(code)

        def order(code):
            return int(code[:4]) * 2 + int(code.endswith("FALL"))

        orders = [order(t.code) for t in self.terms]
        if any(b != a + 1 for a, b in zip(orders, orders[1:])):
            raise ValueError("Terms must be consecutive and ordered")
        for term in self.terms:
            if len({o.id for o in term.options}) != len(term.options):
                raise ValueError("Duplicate option IDs")
            for o in term.options:
                if o.courseCode not in by_code or o.credits != by_code[o.courseCode].credits:
                    raise ValueError("Option does not match catalog")
                if term.published != bool(o.days):
                    raise ValueError("Published meetings / tentative options must be explicit")
        return self


class DegreeSelection(StrictModel):
    termCode: str
    selectionIds: list[str]


class DegreeReply(StrictModel):
    status: Literal["FEASIBLE", "INFEASIBLE", "TIMEOUT", "ERROR", "NO_SOLUTION"]
    selections: list[DegreeSelection] = Field(default_factory=list)
    optimal: bool = False
    completionTerms: int | None = None
    completionLowerBound: int | None = None
