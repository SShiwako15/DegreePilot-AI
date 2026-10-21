# DegreePilot AI — user experience blueprint

## Design intent
A polished, accessible university SaaS application: calm navy/indigo accents, off-white surfaces, strong type hierarchy, substantial whitespace, useful data visualization and readable course cards. Avoid dummy charts, meaningless 100% progress rings, fake live statistics and institutional logos. Use real values from the synthetic database only.

## Information architecture
- Global navigation: Dashboard, Degree Audit, Course Explorer, Semester Planner, AI Advisor; role-scoped Advisor Workspace and Admin Catalog links.
- Student dashboard: greeting, catalog year, "35 / 120 credits" for seeded Ava, progress bar, core courses completed versus remaining, 3–5 next actions, upcoming sections, and privacy-aware account menu.
- Degree audit: each requirement bucket (core 44, CS elective 12, general education 36, free elective 28), credit earned vs required, core checklist, reasons for missing requirements, timestamp+catalog version. Explicitly label "Demo catalog—not official advising".
- Course explorer: search, category/term filters, course details, prerequisite explanation with eligibility reason chips and link to corresponding policy citation.
- Semester planner: term selector, min/max credits, timetable view, hard constraint issues, generated alternatives. Show infeasible reasons and mark projected future offerings tentative.
- AI advisor: cited messages, linked source excerpts, warning that AI does not change records; missing API key yields a clear disabled banner with navigation to manual degree audit.
- Advisor: only assigned fictional advisees; admin: catalog edit/publish actions with confirmation and audit trail.

## Interaction and a11y requirements
- Mobile layout with collapsible sidebar, readable 320px width and no horizontal overflow for content except intentionally scrollable tables.
- Buttons and inputs use descriptive accessible names; form labels and help/errors; keyboard focus is visible; skip navigation, usable tab order and appropriate semantic headings.
- Ensure contrast and do not communicate eligibility only by color; show text like "Blocked: missing CS310".
- Handle loading, expired session, not-found, empty course list, absent offerings, AI disabled and planner timeout states.
- Use skeleton/placeholder only while actual data loads; no fabricated analytics, testimonials, badges, rankings or student images.

## Portfolio-quality deliverables
Take browser screenshots of core flows at desktop and mobile widths after they are implemented (not mock images). Capture a short demo narration in README: 1) sign in 2) degree audit 3) check prerequisite 4) generate plan 5) ask sourced policy question 6) show user isolation test. Record actual verification results and include architecture diagram.
