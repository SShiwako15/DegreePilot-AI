import { useState } from "react";
import { displayCatalogVersion, mutate } from "../api";
import type { PlanResult, RuleIssue } from "../planning";
import { action, field } from "../planning";
import { panel } from "../ui";
import { ErrorMessage } from "./Brand";

interface Roadmap {
  status: string;
  catalogVersion: string;
  inputFingerprint: string;
  terms: PlanResult[];
  issues: RuleIssue[];
  optimal: boolean;
  completionTerms: number | null;
  completionLowerBound: number | null;
  conditionalDegreeComplete: boolean;
}
export function DegreeRoadmap() {
  const [start, setStart] = useState("2027-SPRING");
  const [horizon, setHorizon] = useState(8);
  const [minimum, setMinimum] = useState(12);
  const [maximum, setMaximum] = useState(18);
  const [result, setResult] = useState<Roadmap | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function generate() {
    setBusy(true);
    setError("");
    setResult(null);
    try {
      setResult(
        await mutate<Roadmap>("/api/degree-plans/generate", {
          startTermCode: start,
          horizonTerms: horizon,
          minCredits: minimum,
          maxCredits: maximum,
        }),
      );
    } catch (e) {
      setError(e instanceof Error ? e.message : "Roadmap unavailable.");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section
      className={`${panel} mt-6`}
      aria-label="Multi-semester research planner"
    >
      <h2 className="text-lg font-semibold">Multi-semester research planner</h2>
      <p className="my-3 text-sm leading-6 text-bms-muted">
        Synthetic scenario, not a graduation prediction. This bounded proposal
        assumes every planned course is passed with C or better. Future
        unpublished offerings are tentative; no seats are reserved and no
        records are changed.
      </p>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          void generate();
        }}
      >
        <fieldset disabled={busy} className="grid gap-4 sm:grid-cols-2">
          <legend className="mb-3 text-sm font-semibold">
            Roadmap constraints
          </legend>
          <label className="text-sm">
            First term (YYYY-SPRING or YYYY-FALL)
            <input
              className={field}
              required
              pattern="[0-9]{4}-(SPRING|FALL)"
              value={start}
              onChange={(e) => {
                setStart(e.target.value);
                setResult(null);
              }}
            />
          </label>
          <label className="text-sm">
            Roadmap horizon (semesters)
            <input
              className={field}
              type="number"
              min={1}
              max={12}
              required
              value={horizon}
              onChange={(e) => {
                setHorizon(Number(e.target.value));
                setResult(null);
              }}
            />
          </label>
          <label className="text-sm">
            Roadmap minimum credits
            <input
              className={field}
              type="number"
              min={1}
              max={30}
              required
              value={minimum}
              onChange={(e) => {
                setMinimum(Number(e.target.value));
                setResult(null);
              }}
            />
          </label>
          <label className="text-sm">
            Roadmap maximum credits
            <input
              className={field}
              type="number"
              min={minimum}
              max={30}
              required
              value={maximum}
              onChange={(e) => {
                setMaximum(Number(e.target.value));
                setResult(null);
              }}
            />
          </label>
        </fieldset>
        <button className={`${action} my-4`} disabled={busy}>
          {busy ? "Solving bounded roadmap…" : "Generate degree roadmap"}
        </button>
      </form>
      {error && <ErrorMessage message={error} />}
      {result && (
        <div
          role="region"
          aria-label="Degree roadmap result"
          className="space-y-4"
        >
          <h3 className="font-semibold" role="status">
            {result.status === "FEASIBLE"
              ? "Validated conditional degree roadmap"
              : result.status}
          </h3>
          {result.issues.map((i, index) => (
            <p key={index} className="text-sm">
              {i.code}: {i.message}
            </p>
          ))}
          {result.status === "FEASIBLE" && (
            <>
              <p className="text-sm">
                Completion within {result.completionTerms} semesters, including
                any waiting semesters.{" "}
                {result.optimal
                  ? "The solver proved the modeled objective optimal."
                  : "Feasible incumbent; optimality is not proved."}{" "}
                This is conditional on successful future grades and projected
                availability.
              </p>
              {result.terms.map((t) => (
                <article
                  key={t.termCode}
                  className="rounded-lg border border-slate-200 p-4"
                >
                  <h4 className="text-sm font-semibold">
                    {t.termCode} · {t.totalCredits} credits ·{" "}
                    {t.tentative
                      ? "Tentative offerings — meeting times unconfirmed"
                      : "Published sections checked for conflicts"}
                  </h4>
                  <ul className="mt-2 space-y-2 text-sm">
                    {t.selections.map((o) => (
                      <li key={o.id}>
                        {o.sectionCode ?? o.courseCode} · {o.title} ·{" "}
                        {o.credits} credits
                      </li>
                    ))}
                  </ul>
                </article>
              ))}
              <p className="break-all text-xs text-bms-muted">
                Pinned catalog: {displayCatalogVersion(result.catalogVersion)}
                <br />
                Input fingerprint: {result.inputFingerprint}
              </p>
            </>
          )}
        </div>
      )}
    </section>
  );
}
