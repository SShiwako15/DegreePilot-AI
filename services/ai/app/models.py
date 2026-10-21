from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

Day = Literal["Mon", "Tue", "Wed", "Thu", "Fri"]


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


class Option(StrictModel):
    id: str = Field(min_length=1, max_length=150)
    courseCode: str = Field(min_length=1, max_length=30)
    credits: int = Field(ge=1, le=30)
    core: bool
    days: list[Day] = Field(max_length=5)
    startMinute: int | None = Field(default=None, ge=0, lt=1440)
    endMinute: int | None = Field(default=None, ge=0, le=1440)

    @model_validator(mode="after")
    def valid_meeting(self):
        if len(set(self.days)) != len(self.days):
            raise ValueError("Duplicate weekdays")
        if self.days:
            if self.startMinute is None or self.endMinute is None or self.startMinute >= self.endMinute:
                raise ValueError("Meeting times must be ordered")
        elif self.startMinute is not None or self.endMinute is not None:
            raise ValueError("Tentative courses have no meeting times")
        return self


class SolveRequest(StrictModel):
    options: list[Option] = Field(max_length=300)
    minCredits: int = Field(ge=1, le=30)
    maxCredits: int = Field(ge=1, le=30)
    preferredDays: list[Day] = Field(default_factory=list, max_length=5)
    fewerCampusDays: bool = True
    maxTimeSeconds: float = Field(default=5.0, ge=0, le=5)

    @model_validator(mode="after")
    def valid_request(self):
        if self.minCredits > self.maxCredits:
            raise ValueError("Credit bounds are reversed")
        if len({o.id for o in self.options}) != len(self.options):
            raise ValueError("Duplicate option IDs")
        return self


class SolveResult(StrictModel):
    status: Literal["FEASIBLE", "INFEASIBLE", "TIMEOUT", "ERROR"]
    selectionIds: list[str] = Field(default_factory=list)
