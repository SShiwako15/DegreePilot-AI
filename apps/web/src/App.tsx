import { useEffect, useRef, useState } from "react";
import {
  BookOpen,
  ChevronRight,
  LogOut,
  Menu,
  ShieldCheck,
  X,
} from "lucide-react";
import { ApiError, displayCatalogVersion, mutate, request } from "./api";
import type { Account, AcademicData, Audit, Course, Transcript } from "./api";
import { currentView, views } from "./navigation";
import type { View } from "./navigation";
import {
  Brand,
  DemoBadge,
  ErrorMessage,
  SkipLink,
  UniversityLogo,
} from "./components/Brand";
import { Login } from "./components/Login";
import {
  Buckets,
  CoreList,
  Dashboard,
  ProgressSummary,
  SectionTitle,
  TranscriptView,
} from "./components/AcademicViews";
import { CourseExplorer } from "./components/CourseExplorer";
import { SemesterPlanner } from "./components/SemesterPlanner";
import { AdvisorChat } from "./components/AdvisorChat";
import { AdvisorWorkspace } from "./components/AdvisorWorkspace";
import { AdminWorkspace } from "./components/AdminWorkspace";
import { DegreeRoadmap } from "./components/DegreeRoadmap";
import { eyebrow, iconButton, panel, secondaryButton } from "./ui";

export default function App() {
  const [account, setAccount] = useState<Account | null>(null);
  const [data, setData] = useState<AcademicData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [view, setView] = useState<View>(currentView);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [signingOut, setSigningOut] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const menuButton = useRef<HTMLButtonElement>(null);
  const allowedViews = views.filter((v) =>
    account?.role === "admin"
      ? v.id === "admin"
      : account?.role === "advisor"
        ? v.id === "advising"
        : v.id !== "admin" && v.id !== "advising",
  );
  const activeView = allowedViews.some((v) => v.id === view)
    ? view
    : account?.role === "admin"
      ? "admin"
      : account?.role === "advisor"
        ? "advising"
        : "dashboard";

  useEffect(() => {
    const onHash = () => {
      setView(currentView());
      setMobileOpen(false);
    };
    window.addEventListener("hashchange", onHash);
    return () => window.removeEventListener("hashchange", onHash);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    async function load() {
      setLoading(true);
      setData(null);
      setError("");
      try {
        const actor = await request<Account>("/api/me", options);
        if (controller.signal.aborted) return;
        setAccount(actor);
        if (actor.role === "student") {
          const [audit, transcript, courses] = await Promise.all([
            request<Audit>("/api/students/me/degree-audit", options),
            request<Transcript>("/api/students/me/transcript", options),
            request<Course[]>(
              `/api/catalog/${encodeURIComponent(actor.catalogVersion!)}/courses`,
              options,
            ),
          ]);
          if (!controller.signal.aborted)
            setData({ audit, transcript, courses });
        }
      } catch (cause) {
        if (controller.signal.aborted) return;
        if (cause instanceof ApiError && cause.status === 401) setAccount(null);
        else
          setError(
            cause instanceof Error
              ? cause.message
              : "The service is unavailable.",
          );
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    }
    void load();
    return () => controller.abort();
  }, [refresh]);

  function navigate(next: View) {
    location.hash = next;
    setView(next);
    setMobileOpen(false);
    requestAnimationFrame(() => heading.current?.focus());
  }

  async function logout() {
    setSigningOut(true);
    try {
      await mutate("/api/auth/logout");
      setAccount(null);
      setData(null);
      setError("");
      setRefresh((value) => value + 1);
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 401) {
        setAccount(null);
        setData(null);
      } else
        setError(
          cause instanceof Error
            ? cause.message
            : "Sign out failed. Please retry.",
        );
    } finally {
      setSigningOut(false);
    }
  }

  if (!account)
    return (
      <>
        <SkipLink />
        <Login
          loading={loading}
          error={error}
          onLogin={() => setRefresh((value) => value + 1)}
          onRetry={() => setRefresh((value) => value + 1)}
        />
      </>
    );
  const title = views.find((item) => item.id === activeView)!.label;
  const chatView = account.role === "student" && view === "advisor";
  const description =
    view === "dashboard"
      ? "See how far you’ve come. Know what comes next."
      : view === "audit"
        ? "Every requirement, with the rules behind it."
        : view === "transcript"
          ? "Your complete course history, including every attempt."
          : view === "planner"
            ? "A valid next semester starts with clear constraints."
            : view === "advisor"
              ? "Understand your audit with approved sources beside every answer."
              : "Explore courses and machine-checked eligibility in your catalog.";

  return (
    <div className={chatView ? "flex h-dvh flex-col overflow-hidden" : "min-h-dvh"}>
      <SkipLink />
      <div className="flex h-16 shrink-0 items-center justify-between bg-bms-navy px-4 text-white lg:hidden">
        <Brand />
        <button
          ref={menuButton}
          className={iconButton}
          aria-label={mobileOpen ? "Close navigation" : "Open navigation"}
          aria-expanded={mobileOpen}
          aria-controls="sidebar"
          onClick={() => setMobileOpen(!mobileOpen)}
        >
          {mobileOpen ? <X /> : <Menu />}
        </button>
      </div>
      <aside
        id="sidebar"
        className={`absolute inset-x-0 top-16 z-30 max-h-[calc(100dvh-4rem)] flex-col overflow-y-auto border-b-4 border-bms-orange bg-bms-navy px-5 py-5 text-white shadow-lg lg:fixed lg:inset-y-0 lg:left-0 lg:right-auto lg:flex lg:max-h-none lg:w-64 lg:border-b-0 lg:py-8 lg:shadow-none ${mobileOpen ? "flex" : "hidden"}`}
        onKeyDown={(event) => {
          if (event.key === "Escape") {
            setMobileOpen(false);
            menuButton.current?.focus();
          }
        }}
      >
        <div className="hidden lg:block">
          <Brand />
        </div>
        <div className="my-6 flex items-center gap-3 border-y border-white/15 py-5 lg:mt-9">
          <UniversityLogo className="size-16 rounded-lg bg-white" />
          <div className="text-xs font-semibold leading-5">
            Blue Mountain State
            <br />
            University
            <span className="mt-1 block text-[10px] font-normal text-blue-200">
              Fictional institution
            </span>
          </div>
        </div>
        <p className="mb-3 px-3 text-[10px] font-semibold tracking-[0.18em] text-blue-200">
          YOUR WORKSPACE
        </p>
        <nav aria-label="Main navigation" className="flex flex-col gap-1.5">
          {allowedViews.map((item) => (
            <a
              key={item.id}
              href={`#${item.id}`}
              aria-current={activeView === item.id ? "page" : undefined}
              onClick={(event) => {
                event.preventDefault();
                navigate(item.id);
              }}
              className={`flex min-h-12 items-center gap-3 rounded-lg border-l-3 px-3 py-3 text-xs ${activeView === item.id ? "border-bms-orange bg-white/10 font-semibold text-white" : "border-transparent text-blue-100 hover:bg-white/5"}`}
            >
              <item.icon
                size={18}
                className={activeView === item.id ? "text-bms-orange" : ""}
              />
              <span>{item.label}</span>
            </a>
          ))}
        </nav>
        <div className="mt-auto pt-6 lg:pt-10">
          <div className="mb-5 hidden rounded-xl border border-white/15 bg-white/5 p-4 lg:block">
            <ShieldCheck size={19} className="mb-2 text-bms-orange" />
            <strong className="text-xs">A space to explore.</strong>
            <p className="mt-2 text-[11px] leading-5 text-blue-200">
              Fictional records. Real clarity.
              <br />
              Unofficial Blue Mountain State University fan application.
            </p>
          </div>
          <div className="flex items-center gap-2 border-t border-white/15 pt-5">
            <span className="grid size-9 shrink-0 place-items-center rounded-full border border-white/20 bg-white/10 text-xs font-semibold text-orange-200">
              {account.displayName
                .split(" ")
                .slice(0, 2)
                .map((name) => name[0])
                .join("")}
            </span>
            <div className="min-w-0 flex-1">
              <strong className="text-xs">
                {account.displayName.replace(" (fictional)", "")}
              </strong>
              <small className="mt-1 block text-[10px] text-blue-200">
                {account.role}
              </small>
            </div>
            <button
              className={iconButton}
              aria-label="Sign out"
              title="Sign out"
              disabled={signingOut}
              onClick={() => void logout()}
            >
              <LogOut size={18} />
            </button>
          </div>
        </div>
      </aside>
      <div className={chatView ? "flex min-h-0 flex-1 flex-col lg:ml-64" : "lg:ml-64"}>
        <header className="flex min-h-16 shrink-0 flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 py-3 sm:px-8 lg:px-10">
          <div className="flex items-center gap-2 text-[11px] sm:gap-3">
            <span className="text-bms-muted">My workspace</span>
            <ChevronRight size={13} className="text-slate-400" />
            <strong className="font-medium">{title}</strong>
          </div>
          <DemoBadge />
        </header>
        <main
          id="main"
          tabIndex={-1}
          className={chatView ? "mx-auto flex min-h-0 w-full max-w-7xl flex-1 flex-col px-4 pt-4 sm:px-8 lg:px-10" : "mx-auto max-w-7xl px-4 py-7 sm:px-8 lg:px-10 lg:py-9"}
        >
          <div className={`${chatView ? "mb-3 shrink-0" : "mb-7"} flex flex-wrap items-center justify-between gap-4`}>
            <div>
              <p className={`${chatView ? "hidden" : "mb-2"} ${eyebrow}`}>
                BLUE MOUNTAIN STATE UNIVERSITY
              </p>
              <h1
                ref={heading}
                tabIndex={-1}
                className="rounded-sm text-2xl font-semibold tracking-tight sm:text-3xl"
              >
                {activeView === "dashboard"
                  ? `Your next chapter, ${account.displayName.split(" ")[0]}.`
                  : title}
              </h1>
              <p className={`${chatView ? "hidden" : "mt-2"} text-xs leading-6 text-bms-muted sm:text-sm`}>
                {account.role === "student"
                  ? description
                  : "Scoped access. Reviewed changes. A clear history."}
              </p>
            </div>
            {account.catalogVersion && (
              <span className="hidden items-center gap-2 rounded-lg border border-slate-200 bg-white px-3 py-2 text-[11px] text-bms-muted xl:flex">
                <BookOpen size={15} className="text-bms-blue" />
                {displayCatalogVersion(account.catalogVersion)}
              </span>
            )}
          </div>
          {error && (
            <>
              <ErrorMessage message={error} />
              <button
                className={secondaryButton}
                onClick={() => setRefresh((value) => value + 1)}
              >
                Try again
              </button>
            </>
          )}
          {loading && (
            <div
              className="rounded-xl border border-blue-100 bg-bms-sky p-10 text-center text-sm text-bms-blue"
              role="status"
            >
              Loading your academic records…
            </div>
          )}
          {!loading && account.role === "advisor" && (
            <AdvisorWorkspace key={account.id} />
          )}
          {!loading && account.role === "admin" && (
            <AdminWorkspace key={account.id} />
          )}
          {!loading && data && (
            <>
              {data.audit.status === "invalid" ? (
                <section className={`${panel} mb-6`} role="alert">
                  <h2 className="text-lg font-semibold">
                    We couldn’t calculate this audit
                  </h2>
                  <p className="mt-3 text-sm leading-6 text-bms-muted">
                    No credits or completion decision are shown because the
                    source records need attention.
                  </p>
                  <ul className="mt-4 list-inside list-disc space-y-2 text-sm text-red-900">
                    {data.audit.issues.map((issue, index) => (
                      <li key={index}>
                        <strong>{issue.code}</strong>: {issue.message}
                      </li>
                    ))}
                  </ul>
                </section>
              ) : (
                <>
                  {activeView === "dashboard" && (
                    <Dashboard
                      account={account}
                      data={data}
                      navigate={navigate}
                    />
                  )}
                  {view === "audit" && (
                    <div className="space-y-6">
                      <ProgressSummary audit={data.audit} />
                      <section className={panel}>
                        <SectionTitle
                          label="DEGREE REQUIREMENTS"
                          title="Four paths. One degree."
                        />
                        <Buckets buckets={data.audit.buckets} />
                        <p className="mt-6 text-xs leading-6 text-bms-muted">
                          Every category and the total credit minimum must be
                          met. Each course counts in exactly one category.
                        </p>
                      </section>
                      <CoreList courses={data.audit.coreRequirements} />
                    </div>
                  )}
                </>
              )}
              {view === "transcript" && (
                <TranscriptView
                  transcript={data.transcript}
                  audit={data.audit}
                />
              )}
              {view === "catalog" && <CourseExplorer key={account.id} />}
              {view === "planner" && (
                <>
                  <SemesterPlanner key={account.id} />
                  <DegreeRoadmap key={`roadmap-${account.id}`} />
                </>
              )}
              {view === "advisor" && (
                <AdvisorChat
                  key={account.id}
                  catalogVersion={account.catalogVersion!}
                />
              )}
              {!chatView && <footer className="mt-7 flex flex-wrap items-start justify-between gap-3 border-t border-slate-200 pt-5 text-[10px] leading-5 text-bms-muted">
                <span className="flex items-center gap-2">
                  <ShieldCheck size={14} className="shrink-0 text-bms-blue" />
                  Fictional catalog — not official advising
                </span>
                <span>
                  {displayCatalogVersion(data.audit.catalogVersion)} · Rules{" "}
                  {data.audit.ruleEngineVersion}
                  <br />
                  Calculated{" "}
                  <time dateTime={data.audit.generatedAt}>
                    {new Date(data.audit.generatedAt).toLocaleString()}
                  </time>
                </span>
              </footer>}
            </>
          )}
        </main>
      </div>
    </div>
  );
}
