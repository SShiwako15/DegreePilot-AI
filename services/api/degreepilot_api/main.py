import hmac
import secrets
import time
from contextlib import asynccontextmanager
from urllib.parse import urlsplit
from uuid import UUID, uuid4

from fastapi import Depends, FastAPI, HTTPException, Request, Response
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from sqlalchemy import select
from sqlalchemy.exc import DBAPIError
from sqlalchemy.orm.exc import StaleDataError
from starlette.middleware.sessions import SessionMiddleware

from . import advisor, management, planning
from . import models as m
from . import schemas as s
from .database import (
    degree_audit,
    load_context,
    make_engine,
    public,
    sessions,
    term_model,
)
from .domain import context_issues, eligibility, options, term_order, validate_plan
from .gateway import Gateway
from .migrate import migrate
from .policies import Sources
from .security import passwords, verify_password
from .seed import seed
from .settings import Settings

LIMIT = 512 * 1024


def problem(status, title, code=None):
    return JSONResponse(
        status_code=status,
        content={"status": status, "title": title, **({"code": code} if code else {})},
        media_type="application/problem+json",
    )


def db_session(request: Request):
    with request.app.state.sessions() as db:
        yield db


def account(request: Request):
    if not request.state.actor:
        raise HTTPException(401, "Sign in to access this resource.")
    return request.state.actor


def role(name):
    def required(actor=Depends(account)):
        if actor["role"] != name:
            raise HTTPException(403, "This workspace is restricted to its assigned role.")
        return actor

    return required


student = role("student")
admin = role("admin")
assigned_advisor = role("advisor")


def assigned(db, actor, student_id):
    row = db.get(m.Assignment, (actor, student_id))
    return row is not None and row.active


def readable(db, actor, student_id):
    if actor["role"] not in {"student", "advisor"}:
        raise HTTPException(403, "Academic records require student or advisor access.")
    allowed = (
        actor["id"] == student_id if actor["role"] == "student" else assigned(db, actor["id"], student_id)
    )
    if not allowed:
        raise HTTPException(404, "Resource not found.")


def found(value):
    if value is None:
        raise HTTPException(404, "Resource not found.")
    return value


def create_app(settings: Settings | None = None, engine=None, gateway=None) -> FastAPI:
    settings = settings or Settings()
    signing_key = settings.signing_key()
    engine = engine or make_engine(settings.database)
    factory = sessions(engine)
    sources = Sources()

    @asynccontextmanager
    async def lifespan(_app):
        migrate(engine)
        if settings.demo_enabled:
            with factory.begin() as db:
                seed(db, settings.demo_password, sources)
        yield
        engine.dispose()

    app = FastAPI(
        title="DegreePilot academic API", lifespan=lifespan, docs_url=None, redoc_url=None, openapi_url=None
    )
    app.state.sessions = factory
    app.state.engine = engine
    app.state.gateway = gateway or Gateway(settings.planner_url)
    app.state.sources = sources
    app.state.metrics = dict(requests=0, serverErrors=0, rateLimited=0)
    started = time.monotonic()
    budgets = {}

    @app.exception_handler(HTTPException)
    async def http_error(_request, error):
        return problem(error.status_code, str(error.detail))

    @app.exception_handler(RequestValidationError)
    async def invalid_request(_request, _error):
        # Validation errors can contain the supplied body; only a fixed message is public.
        return problem(400, "The request body or parameters are invalid.")

    @app.middleware("http")
    async def boundary(request: Request, call_next):
        response = None
        try:
            actor_id = request.session.get("user")
            request.state.actor = None
            if actor_id and time.time() - request.session.get("issued", 0) < 7200:
                with factory() as db:
                    user = db.get(m.User, actor_id)
                    if user:
                        request.state.actor = dict(id=user.id, role=user.role, displayName=user.display_name)
            elif actor_id:
                request.session.clear()
            public_path = request.url.path in {
                "/api/health",
                "/health/ready",
                "/api/auth/csrf",
                "/api/auth/login",
            }
            if request.url.path.startswith("/api/") and not public_path and not request.state.actor:
                response = problem(401, "Sign in to access this resource.")
            else:
                category = None
                if request.url.path == "/api/auth/login":
                    category, limit = "login", settings.login_limit
                elif request.url.path == "/api/advisor/chat":
                    category, limit = "advisor", settings.advisor_limit
                elif request.url.path in {"/api/plans/generate", "/api/degree-plans/generate"}:
                    category, limit = "planner", settings.planner_limit
                if category and request.method == "POST":
                    window = int(time.monotonic() // 60)
                    for key in list(budgets):
                        if key[2] != window:
                            del budgets[key]
                    identity = (
                        request.client.host
                        if category == "login" and request.client
                        else actor_id or "anonymous"
                    )
                    key = (category, identity, window)
                    budgets[key] = budgets.get(key, 0) + 1
                    if budgets[key] > max(1, min(limit, 1000)):
                        response = problem(
                            429, "Request limit reached. Please wait a minute before trying again."
                        )
                        response.headers["Retry-After"] = "60"
                if response is None and request.method in {"POST", "PUT", "PATCH", "DELETE"}:
                    origin = request.headers.get("origin")
                    if origin and (
                        urlsplit(origin).scheme != request.url.scheme
                        or urlsplit(origin).netloc.lower() != request.url.netloc.lower()
                    ):
                        response = problem(400, "Cross-origin mutations are not allowed.")
                    token, expected = request.headers.get("X-CSRF-TOKEN", ""), request.session.get("csrf", "")
                    if response is None and (
                        not expected or not hmac.compare_digest(token.encode(), expected.encode())
                    ):
                        response = problem(400, "Refresh the page and retry the request.", "CSRF_INVALID")
                if response is None:
                    body = bytearray()
                    async for chunk in request.stream():
                        body.extend(chunk)
                        if len(body) > LIMIT:
                            response = problem(413, "Request exceeds the 512 KiB limit.")
                            break
                    if response is None:
                        request._body = bytes(body)
                        response = await call_next(request)
        except (DBAPIError, StaleDataError):
            response = problem(
                409, "The data changed or conflicts with existing records. Refresh before retrying."
            )
        except Exception:
            response = problem(500, "The request could not be completed.", "SERVICE_ERROR")
        for key, value in {
            "Cache-Control": "no-store",
            "X-Content-Type-Options": "nosniff",
            "Referrer-Policy": "same-origin",
            "X-Frame-Options": "DENY",
            "X-Request-ID": str(uuid4()),
        }.items():
            response.headers[key] = value
        metrics = app.state.metrics
        metrics["requests"] += 1
        metrics["serverErrors"] += int(response.status_code >= 500)
        metrics["rateLimited"] += int(response.status_code == 429)
        return response

    # Outermost middleware makes the signed session available to the security boundary.
    app.add_middleware(
        SessionMiddleware,
        secret_key=signing_key,
        session_cookie="degreepilot.session",
        max_age=7200,
        same_site="strict",
        https_only=not settings.local,
    )

    @app.get("/api/health")
    def health():
        return {"status": "healthy", "service": "degreepilot-api"}

    @app.get("/health/ready")
    def ready():
        try:
            with factory() as db:
                for table in m.Base.metadata.sorted_tables:
                    db.execute(select(table).limit(1))
            return {"status": "ready"}
        except Exception:
            return JSONResponse(status_code=503, content={"status": "unavailable"})

    @app.get("/api/auth/csrf")
    def csrf(request: Request):
        if "csrf" not in request.session:
            request.session["csrf"] = secrets.token_urlsafe(32)
        return {"token": request.session["csrf"]}

    @app.post("/api/auth/login", status_code=204)
    def login(body: s.LoginRequest, request: Request, db=Depends(db_session)):
        if not settings.demo_enabled:
            raise HTTPException(503, "Local demo sign-in is disabled.")
        user = db.get(m.User, body.username)
        valid, upgrade = verify_password(body.password, user.password_hash) if user else (False, False)
        if not valid:
            # Keep missing-account work comparable to password verification.
            if user is None:
                passwords.hash(body.password)
            raise HTTPException(401, "The username or password is incorrect.")
        if upgrade:
            user.password_hash = passwords.hash(body.password)
            db.commit()
        request.session.clear()
        request.session.update(user=user.id, issued=time.time(), csrf=secrets.token_urlsafe(32))
        return Response(status_code=204)

    @app.post("/api/auth/logout", status_code=204)
    def logout(request: Request, _actor=Depends(account)):
        request.session.clear()
        return Response(status_code=204)

    @app.get("/api/me")
    async def me(actor=Depends(account), db=Depends(db_session)):
        profile = db.get(m.Student, actor["id"])
        status = (
            await app.state.gateway.status()
            if actor["role"] == "student"
            else {"available": False, "reason": "AI advising is restricted to the student's own workspace."}
        )
        return {**actor, "catalogVersion": profile.catalog_version if profile else None, "ai": status}

    @app.get("/api/catalog/versions")
    def versions(_actor=Depends(account), db=Depends(db_session)):
        return [
            dict(catalogVersion=c.id, programId=c.program_id)
            for c in db.scalars(
                select(m.Catalog).where(m.Catalog.status == "published").order_by(m.Catalog.id)
            )
        ]

    @app.get("/api/catalog/{version}/courses")
    def catalog_courses(version: str, _actor=Depends(account), db=Depends(db_session)):
        from .database import course_model

        edition = found(db.get(m.Catalog, version))
        if edition.status != "published":
            raise HTTPException(404, "Resource not found.")
        return [
            course_model(c)
            for c in db.scalars(
                select(m.Course).where(m.Course.catalog_version == version).order_by(m.Course.code)
            )
        ]

    def transcript(db, student_id):
        profile = found(db.get(m.Student, student_id))
        rows = list(
            db.execute(
                select(m.Attempt, m.Course)
                .join(
                    m.Course,
                    (m.Attempt.catalog_version == m.Course.catalog_version)
                    & (m.Attempt.course_code == m.Course.code),
                )
                .where(m.Attempt.student_id == student_id)
            )
        )
        return dict(
            catalogVersion=profile.catalog_version,
            attempts=[
                dict(
                    courseCode=a.course_code,
                    title=c.title,
                    credits=c.credits,
                    term=a.term,
                    grade=a.grade,
                )
                for a, c in sorted(rows, key=lambda row: (-term_order(row[0].term), row[0].course_code))
            ],
        )

    @app.get("/api/students/me/transcript")
    def own_transcript(actor=Depends(student), db=Depends(db_session)):
        return transcript(db, actor["id"])

    @app.get("/api/students/me/degree-audit")
    def own_audit(actor=Depends(student), db=Depends(db_session)):
        return found(degree_audit(db, actor["id"]))

    @app.get("/api/students/{student_id}/transcript")
    def other_transcript(student_id: str, actor=Depends(account), db=Depends(db_session)):
        readable(db, actor, student_id)
        return transcript(db, student_id)

    @app.get("/api/students/{student_id}/degree-audit")
    def other_audit(student_id: str, actor=Depends(account), db=Depends(db_session)):
        readable(db, actor, student_id)
        return found(degree_audit(db, student_id))

    @app.get("/api/terms")
    def terms(_actor=Depends(student), db=Depends(db_session)):
        return [term_model(t) for t in db.scalars(select(m.Term).order_by(m.Term.starts_on))]

    @app.get("/api/courses/{code}/eligibility")
    def course_eligibility(code: str, term: str, actor=Depends(student), db=Depends(db_session)):
        return eligibility(found(load_context(db, actor["id"], term)), code)

    @app.get("/api/courses")
    def course_search(
        term: str,
        search: str | None = None,
        category: str | None = None,
        status: str | None = None,
        actor=Depends(student),
        db=Depends(db_session),
    ):
        context = found(load_context(db, actor["id"], term))
        issues = context_issues(context)
        if issues:
            return JSONResponse(status_code=422, content={"issues": issues})
        rows = []
        for course in context.courses:
            if (
                search
                and search.lower() not in (course.code + " " + course.title).lower()
                or category
                and course.category != category
            ):
                continue
            result = eligibility(context, course.code)
            if not status or result["status"] == status:
                rows.append(dict(course=course.model_dump(by_alias=True), eligibility=result))
        return rows

    @app.get("/api/terms/{term_code}/sections")
    def sections(term_code: str, actor=Depends(student), db=Depends(db_session)):
        context = found(load_context(db, actor["id"], term_code))
        return dict(
            catalogVersion=context.version,
            term=context.term,
            options=options(context),
            sections=context.sections if context.term.is_published else [],
            issues=context_issues(context),
        )

    @app.post("/api/plans/generate")
    async def generate_plan(body: s.PlanRequest, actor=Depends(student), db=Depends(db_session)):
        context = found(load_context(db, actor["id"], body.term_code))
        return await planning.generate(db, actor["id"], context, body, app.state.gateway)

    @app.post("/api/plans/validate")
    def check_plan(body: s.SelectionRequest, actor=Depends(student), db=Depends(db_session)):
        return validate_plan(
            found(load_context(db, actor["id"], body.constraints.term_code)),
            body.constraints,
            body.selection_ids,
        )

    @app.post("/api/plans/save")
    def save_plan(body: s.SelectionRequest, actor=Depends(student), db=Depends(db_session)):
        result = found(planning.save(db, actor["id"], body))
        return result if result["status"] == "FEASIBLE" else JSONResponse(status_code=422, content=result)

    @app.get("/api/students/me/plans")
    def own_plans(actor=Depends(student), db=Depends(db_session)):
        return plans_for(db, actor["id"])

    def plans_for(db, student_id):
        return [
            planning.read_plan(db, p)
            for p in db.scalars(
                select(m.Plan).where(m.Plan.student_id == student_id).order_by(m.Plan.created_at.desc())
            )
        ]

    @app.get("/api/plans/{plan_id}")
    def plan(plan_id: UUID, actor=Depends(student), db=Depends(db_session)):
        row = found(db.get(m.Plan, plan_id))
        if row.student_id != actor["id"]:
            raise HTTPException(404, "Resource not found.")
        return planning.read_plan(db, row)

    @app.post("/api/degree-plans/generate")
    async def roadmap(body: s.DegreeRequest, actor=Depends(student), db=Depends(db_session)):
        return found(await planning.generate_degree(db, actor["id"], body, app.state.gateway))

    @app.get("/api/advisor/status")
    async def advisor_status(_actor=Depends(student)):
        return await app.state.gateway.status()

    @app.post("/api/advisor/chat")
    async def chat(body: s.ChatRequest, actor=Depends(student), db=Depends(db_session)):
        return found(await advisor.answer(db, actor["id"], body, sources, app.state.gateway))

    @app.get("/api/policies")
    def policies(actor=Depends(student), db=Depends(db_session)):
        profile = found(db.get(m.Student, actor["id"]))
        return sources.read(db, profile.catalog_version)

    @app.get("/api/policies/sections/{source_id}")
    def policy_section(source_id: str, actor=Depends(student), db=Depends(db_session)):
        profile = found(db.get(m.Student, actor["id"]))
        rows = sources.read(db, profile.catalog_version, [source_id])
        return found(rows[0] if len(rows) == 1 else None)

    @app.get("/api/advisor-workspace/students")
    def advisees(actor=Depends(assigned_advisor), db=Depends(db_session)):
        return [
            dict(id=u.id, displayName=u.display_name, catalogVersion=p.catalog_version)
            for u, p in db.execute(
                select(m.User, m.Student)
                .join(m.Student, m.User.id == m.Student.user_id)
                .join(m.Assignment, m.Assignment.student_id == m.Student.user_id)
                .where(m.Assignment.advisor_id == actor["id"], m.Assignment.active)
            )
        ]

    @app.get("/api/advisor-workspace/terms")
    def advisor_terms(_actor=Depends(assigned_advisor), db=Depends(db_session)):
        return [term_model(t) for t in db.scalars(select(m.Term).order_by(m.Term.starts_on))]

    @app.get("/api/advisor-workspace/students/{student_id}/planning/{term}")
    def advisor_planning(student_id: str, term: str, actor=Depends(assigned_advisor), db=Depends(db_session)):
        management.require(assigned(db, actor["id"], student_id), 404, "Resource not found.")
        context = found(load_context(db, student_id, term))
        return dict(
            catalogVersion=context.version,
            term=context.term,
            options=options(context),
            issues=context_issues(context),
        )

    @app.post("/api/advisor-workspace/students/{student_id}/suggestions")
    def suggest(
        student_id: str, body: s.SelectionRequest, actor=Depends(assigned_advisor), db=Depends(db_session)
    ):
        management.require(assigned(db, actor["id"], student_id), 404, "Resource not found.")
        result = found(planning.save(db, student_id, body, suggestion_advisor=actor["id"]))
        if result["status"] != "FEASIBLE":
            return JSONResponse(status_code=422, content=result)
        return dict(id=result["id"], plan=result)

    @app.get("/api/students/{student_id}/plans")
    def target_plans(student_id: str, actor=Depends(account), db=Depends(db_session)):
        readable(db, actor, student_id)
        return plans_for(db, student_id)

    @app.get("/api/students/{student_id}/suggestions")
    def suggestions(student_id: str, actor=Depends(account), db=Depends(db_session)):
        readable(db, actor, student_id)
        import json

        return [
            dict(
                id=str(row.id),
                advisorId=row.advisor_id,
                createdAt=row.created_at,
                constraints=s.PlanRequest.model_validate(
                    {k[0].lower() + k[1:]: v for k, v in json.loads(row.constraints_json).items()}
                ),
                plan=planning.read_plan(db, row),
            )
            for row in db.scalars(
                select(m.Suggestion)
                .where(m.Suggestion.student_id == student_id)
                .order_by(m.Suggestion.created_at.desc())
            )
        ]

    @app.get("/api/admin/directory")
    def directory(_actor=Depends(admin), db=Depends(db_session)):
        return dict(
            users=[
                dict(id=u.id, displayName=u.display_name, role=u.role)
                for u in db.scalars(select(m.User).where(m.User.role != "admin").order_by(m.User.id))
            ],
            assignments=[
                public(a) for a in db.scalars(select(m.Assignment).order_by(m.Assignment.student_id))
            ],
        )

    @app.post("/api/admin/assignments", status_code=204)
    def assignments(body: s.AssignmentRequest, actor=Depends(admin), db=Depends(db_session)):
        management.assign(db, actor["id"], body)
        return Response(status_code=204)

    @app.get("/api/admin/catalogs")
    def catalogs(_actor=Depends(admin), db=Depends(db_session)):
        return [
            dict(
                id=c.id,
                status=c.status,
                revision=c.revision,
                programId=c.program_id,
                approvalHash=c.approval_hash,
            )
            for c in db.scalars(select(m.Catalog).order_by(m.Catalog.id))
        ]

    @app.get("/api/admin/catalogs/{version}")
    def catalog_detail(version: str, _actor=Depends(admin), db=Depends(db_session)):
        from .database import course_model

        row = found(db.get(m.Catalog, version))
        return dict(
            id=row.id,
            status=row.status,
            revision=row.revision,
            program=s.Program.model_validate_json(row.rules_json),
            courses=[
                course_model(c)
                for c in db.scalars(
                    select(m.Course).where(m.Course.catalog_version == version).order_by(m.Course.code)
                )
            ],
        )

    @app.post("/api/admin/catalogs")
    def clone_catalog(body: s.CatalogClone, actor=Depends(admin), db=Depends(db_session)):
        return management.clone_catalog(db, actor["id"], body)

    @app.post("/api/admin/catalogs/{version}/draft", status_code=204)
    def edit_catalog(version: str, body: s.CatalogDraft, actor=Depends(admin), db=Depends(db_session)):
        management.edit_catalog(db, actor["id"], version, body)
        return Response(status_code=204)

    @app.post("/api/admin/catalogs/{version}/publish", status_code=204)
    def publish_catalog(version: str, body: s.Revision, actor=Depends(admin), db=Depends(db_session)):
        management.publish_catalog(db, actor["id"], version, body.revision)
        return Response(status_code=204)

    @app.get("/api/admin/policies")
    def admin_policies(_actor=Depends(admin), db=Depends(db_session)):
        return [
            public(p)
            for p in db.scalars(
                select(m.PolicyDocument).order_by(m.PolicyDocument.catalog_version, m.PolicyDocument.version)
            )
        ]

    @app.get("/api/admin/policies/{doc_id}")
    def admin_policy(doc_id: str, _actor=Depends(admin), db=Depends(db_session)):
        doc = found(db.get(m.PolicyDocument, doc_id))
        return dict(
            document=public(doc),
            sections=[
                dict(id=row.id, title=row.title, text=row.text)
                for row in db.scalars(
                    select(m.PolicySection)
                    .where(m.PolicySection.document_id == doc_id)
                    .order_by(m.PolicySection.id)
                )
            ],
        )

    @app.post("/api/admin/policies")
    def clone_policy(body: s.PolicyClone, actor=Depends(admin), db=Depends(db_session)):
        return management.clone_policy(db, actor["id"], body)

    @app.post("/api/admin/policies/{doc_id}/draft", status_code=204)
    def edit_policy(doc_id: str, body: s.PolicyDraft, actor=Depends(admin), db=Depends(db_session)):
        management.edit_policy(db, actor["id"], doc_id, body)
        return Response(status_code=204)

    @app.post("/api/admin/policies/{doc_id}/publish", status_code=204)
    def publish_policy(doc_id: str, body: s.Revision, actor=Depends(admin), db=Depends(db_session)):
        management.publish_policy(db, actor["id"], doc_id, body.revision)
        return Response(status_code=204)

    @app.get("/api/admin/sections")
    def admin_sections(_actor=Depends(admin), db=Depends(db_session)):
        return [
            public(row) for row in db.scalars(select(m.Section).order_by(m.Section.term_code, m.Section.id))
        ]

    @app.post("/api/admin/sections/seats", status_code=204)
    def set_seats(body: s.SectionSeats, actor=Depends(admin), db=Depends(db_session)):
        management.seats(db, actor["id"], body)
        return Response(status_code=204)

    @app.get("/api/admin/audit")
    def mutation_audit(_actor=Depends(admin), db=Depends(db_session)):
        return [
            public(row)
            for row in db.scalars(
                select(m.MutationAudit).order_by(m.MutationAudit.occurred_at.desc()).limit(100)
            )
        ]

    @app.get("/api/admin/operations")
    def operations(_actor=Depends(admin)):
        return {**app.state.metrics, "uptimeSeconds": time.monotonic() - started}

    return app


app = create_app()
