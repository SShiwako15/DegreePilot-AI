import type { Course } from "./api";

export interface Term {
  code: string;
  name: string;
  kind: string;
  startsOn: string;
  endsOn: string;
  timezone: string;
  isPublished: boolean;
}
export interface RuleIssue {
  code: string;
  message: string;
}
export interface Eligibility {
  courseCode: string;
  status: "eligible" | "blocked" | "completed" | "not_offered" | "invalid";
  eligible: boolean;
  tentative: boolean;
  missingPrerequisiteGroups: string[][];
  reasons: RuleIssue[];
}
export interface CourseEligibility {
  course: Course;
  eligibility: Eligibility;
}
export interface PlanOption {
  id: string;
  courseCode: string;
  title: string;
  credits: number;
  category: string;
  sectionCode: string | null;
  days: string[];
  startLocal: string | null;
  endLocal: string | null;
  timezone: string;
  availableSeats: number | null;
  tentative: boolean;
}
export interface Offerings {
  catalogVersion: string;
  term: Term;
  options: PlanOption[];
  issues: RuleIssue[];
}
export interface PlanRequest {
  termCode: string;
  minCredits: number;
  maxCredits: number;
  preferredDays: string[];
  fewerCampusDays: boolean;
}
export interface PlanResult {
  status:
    | "FEASIBLE"
    | "INFEASIBLE"
    | "TIMEOUT"
    | "UNAVAILABLE"
    | "ERROR"
    | "INVALID";
  catalogVersion: string;
  termCode: string;
  tentative: boolean;
  totalCredits: number;
  selections: PlanOption[];
  issues: RuleIssue[];
  id: string | null;
}
export const weekdays = ["Mon", "Tue", "Wed", "Thu", "Fri"];
export const field =
  "min-h-11 w-full rounded-lg border border-slate-300 bg-white px-3 text-sm text-bms-ink disabled:opacity-60";
export const action =
  "min-h-11 cursor-pointer rounded-lg bg-bms-blue px-4 py-2 text-sm font-semibold text-white hover:bg-bms-navy disabled:cursor-not-allowed disabled:opacity-50";
