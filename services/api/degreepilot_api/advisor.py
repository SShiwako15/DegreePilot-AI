import re

from sqlalchemy import select

from . import models as m
from .database import academic, degree_audit, load_context
from .domain import context_issues, eligibility, now, options
from .schemas import ChatRequest

DISCLAIMER = "Fictional demonstration policies; informational guidance only. Verify real requirements with an academic advisor."
LIMITED = "Replies use only the approved evidence and current deterministic facts supplied for this question."
UNSAFE = re.compile(
    r"ignore.{0,50}(previous|instruction|polic)|system prompt|reveal.{0,40}(key|secret|token)|exfiltrat|(?:change|edit|update|override|delete|alter).{0,60}(grade|transcript|credit|record)|(?:sql|execute code)",
    re.I,
)
PRIVATE = re.compile(r"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|\bsk-[A-Za-z0-9_-]+|\bstudent-[A-Za-z0-9-]+", re.I)


def classify(question, catalog):
    q = question.lower()

    def has(*words):
        return any(word in q for word in words)

    if has("transfer", "tuition", "scholarship", "financial aid", "admission", "medical"):
        return "unsupported", [], []
    if has("grade", "repeat", "passing", "gpa"):
        return "grades", ["CAT-CORE-GRADES"], []
    codes = [re.sub(r"[\s-]", "", c).upper() for c in re.findall(r"\b[A-Z]{2,5}[\s-]?\d{3}\b", q, re.I)]
    for course in catalog:
        title = course.title.lower()
        if re.search(r"\b" + re.escape(title) + r"\b", q) or (
            title.endswith("s") and re.search(r"\b" + re.escape(title[:-1]) + r"\b", q)
        ):
            codes.append(course.code)
    codes = list(dict.fromkeys(codes))
    if codes:
        sources = (
            [f"CAT-PREREQ-{codes[0]}"]
            if len(codes) == 1 and codes[0] in {"CS301", "CS350", "CS401"}
            else ["CAT-PREREQUISITES"]
        )
        return "eligibility", sources, codes
    if has("prereq", "pre-req", "pre req", "eligible", "eligibility", "before taking", "before i take"):
        return "eligibility", ["CAT-PREREQUISITES"], []
    if has("overlap", "conflict", "adjacent", "timezone", "same time", "clash"):
        return "conflicts", ["PLN-SECTION-CONFLICT"], []
    if has(
        "graduat",
        "degree",
        "earned",
        "progress",
        "remaining",
        "still need",
        "left to",
        "on track",
        "how far",
        "what is left",
        "what's left",
        "am i done",
    ):
        return "degree", ["CAT-BSCS-TOTAL", "CAT-GRADUATION"], []
    if has(
        "what classes",
        "which classes",
        "what courses",
        "which courses",
        "classes can i",
        "courses can i",
        "recommend courses",
        "recommend classes",
        "suggest classes",
        "study next",
        "take next",
        "choose courses",
    ):
        return "options", ["CAT-PREREQUISITES", "PLN-FUTURE-OFFERINGS"], []
    if has("tentative", "future", "unpublished", "projected", "offering"):
        return "future", ["PLN-FUTURE-OFFERINGS"], []
    if has("credit load", "credit range", "how many credits should", "how many credits can", "12", "18"):
        return "load", ["PLN-CREDIT-LOAD"], []
    if has("credit", "core courses", "requirements"):
        return "degree", ["CAT-BSCS-TOTAL", "CAT-GRADUATION"], []
    if has("semester", "plan", "schedule", "timetable", "registration"):
        return "load", ["PLN-CREDIT-LOAD", "PLN-SECTION-CONFLICT", "PLN-FUTURE-OFFERINGS"], []
    if has("advisor", "enrollment", "artificial intelligence") or re.search(r"\bai\b", q):
        return "boundary", ["PLN-AI-ROLE"], []
    return "unsupported", [], []


def route(request, catalog):
    if UNSAFE.search(request.question):
        return "refused", [], []
    intent = classify(request.question, catalog)
    if (intent[0] == "unsupported" or intent[0] == "eligibility" and not intent[2]) and re.match(
        r"^(why|what about|can you explain|tell me more|and |how so|am i eligible)", request.question, re.I
    ):
        for prior in reversed(request.previous_questions):
            if UNSAFE.search(prior):
                continue
            contextual = classify(prior + " " + request.question, catalog)
            if contextual[0] != "unsupported":
                return contextual
    return intent


def failure_message(reason):
    reason = reason if isinstance(reason, str) else None
    return {
        "PROVIDER_AUTH": "The AI provider rejected its credentials or model permissions. An administrator must check the private provider configuration. Deterministic features remain available.",
        "PROVIDER_RATE_LIMIT": "The AI provider's quota or rate limit was reached. Check API billing/limits or try again later. Deterministic features remain available.",
        "PROVIDER_TIMEOUT": "The AI provider could not be reached within the response deadline. Try again later; deterministic features remain available.",
        "PROVIDER_CONNECTION": "The AI provider could not be reached within the response deadline. Try again later; deterministic features remain available.",
        "MODEL_INCOMPLETE": "The AI provider returned an incomplete response. No partial academic advice was accepted. Try a narrower question.",
        "MODEL_REFUSED": "The AI provider did not select an approved explanation for this question. Ask an academic advisor.",
        "MODEL_SELECTION_INVALID": "The AI response failed validation. No unverified answer or citations were accepted.",
        "MODEL_OUTPUT_INVALID": "The AI response failed validation. No unverified answer or citations were accepted.",
        "GROUNDING_REJECTED": "The AI response failed validation. No unverified answer or citations were accepted.",
    }.get(
        reason,
        "No trusted answer was returned. The response was unavailable, refused, incomplete, or failed grounding/citation validation.",
    )


async def answer(db, actor: str, request: ChatRequest, sources, gateway):
    data = academic(db, actor)
    if data is None:
        return None
    version, _program, catalog, _attempts = data

    def stop(status, message):
        return dict(
            status=status,
            catalogVersion=version,
            answer=message,
            citations=[],
            limitations=[DISCLAIMER],
            facts=[],
        )

    if request.catalog_version is not None and request.catalog_version != version:
        return stop(
            "ABSTAIN",
            "The requested catalog does not match your pinned catalog. No other edition was consulted.",
        )
    intent, ids, targets = route(request, catalog)
    if intent == "refused":
        return stop(
            "REFUSED",
            "I cannot change records, override requirements, reveal secrets, or follow instructions that conflict with the advising rules. Your academic records are unchanged.",
        )
    if intent == "unsupported":
        return stop(
            "ABSTAIN",
            "The approved demonstration policies do not support that question. Please ask an academic advisor; undocumented rules cannot be inferred.",
        )
    evidence = sources.read(db, version, ids)
    if not evidence or not sources.rules_approved(db, version):
        return stop(
            "ABSTAIN",
            "Approved evidence or its catalog rules are missing, ambiguous, or changed. I cannot provide a grounded answer.",
        )
    if not sources.reviewed(evidence):
        return stop(
            "ABSTAIN",
            "The revised policy text has not been checked against the pinned deterministic rules for AI explanations. Ask an advisor to review the revised source.",
        )
    facts, checked_eligibility = [], []
    audited = checked_options = checked_term = None
    if intent in {"degree", "grades"}:
        audited = degree_audit(db, actor)
        if not audited or audited["status"] != "valid":
            return stop(
                "ABSTAIN",
                "The pinned catalog or academic record is invalid. No graduation determination can be explained.",
            )
        if intent == "degree":
            complete = "complete" if audited["degreeComplete"] else "not yet complete"
            facts.append(
                f"You have {audited['earnedCredits']} of the required {audited['requiredCredits']} unique earned credits. The deterministic audit shows that degree requirements are {complete}."
            )
            facts.append(
                f"You need at least {max(0, audited['requiredCredits'] - audited['earnedCredits'])} more unique earned credits toward the total minimum; every category and required core course must also be satisfied."
            )
            facts.append(
                "Credit categories: "
                + "; ".join(
                    f"{b['label']}: {b['earned']} of {b['required']}; {b['remaining']} remaining"
                    for b in audited["buckets"]
                )
                + "."
            )
            remaining = [
                f"{c['code']} {c['title']}" for c in audited["coreRequirements"] if not c["complete"]
            ]
            facts.append(
                "Required core courses still to complete: " + ", ".join(remaining) + "."
                if remaining
                else "All required core courses are complete."
            )
        else:
            facts.append(
                "Your cumulative GPA is unavailable."
                if audited["gpa"] is None
                else f"Your cumulative GPA is {audited['gpa']:.2f}; it includes all letter-graded attempts, including repeats and D/F grades."
            )
    if intent == "options" or intent == "eligibility" and targets:
        if len(targets) > 4:
            return stop(
                "ANSWERED",
                "Please ask about up to four courses at a time so I can explain each prerequisite clearly.",
            )
        unknown = [code for code in targets if code not in {c.code for c in catalog}]
        if unknown:
            return stop(
                "ABSTAIN",
                f"I could not find {', '.join(unknown)} in your pinned catalog. Please check the course code or use its full catalog title.",
            )
        term = request.term_code or db.scalar(
            select(m.Term.code).where(m.Term.starts_on > now().date()).order_by(m.Term.starts_on).limit(1)
        )
        if not term:
            return stop("ABSTAIN", "Please select a supported planning term before asking about eligibility.")
        context = load_context(db, actor, term)
        if not context:
            return stop("ABSTAIN", "The requested planning term is unavailable.")
        if context_issues(context):
            return stop(
                "ABSTAIN",
                "Eligibility cannot be explained because the pinned catalog or source record is invalid.",
            )
        if intent == "options":
            checked_options = options(context)
            available = {o["courseCode"]: o for o in checked_options}
            facts.append(
                f"For {term}, {len(available)} courses meet your prerequisites and have available offerings. These are individual choices; a combined timetable must still be checked for time conflicts and credit limits."
            )
            rows = [available[c] for c in sorted(available)]
            for offset in range(0, len(rows), 8):
                facts.append(
                    "Eligible courses: "
                    + "; ".join(
                        f"{o['courseCode']} {o['title']} ({o['credits']} credits)"
                        for o in rows[offset : offset + 8]
                    )
                    + "."
                )
            if not available:
                facts.append(
                    "No eligible course offerings are currently available for this term. Try another term or review your missing prerequisites in the course catalog."
                )
        for code in targets:
            course = next(c for c in context.courses if c.code == code)
            result = eligibility(context, code)
            prerequisites = " AND ".join("(" + " OR ".join(g) + ")" for g in course.prerequisite_groups)
            facts.append(
                f"For {term}, {code} is {result['status'].replace('_', ' ')}. {course.title} prerequisites: "
                + (
                    prerequisites + "; complete each group with C or better before the target term."
                    if prerequisites
                    else "no course prerequisites."
                )
            )
            if result["reasons"]:
                facts.append(" ".join(r["message"] for r in result["reasons"]))
            checked_eligibility.append(result)
        if not context.term.is_published:
            facts.append(
                "This term is tentative. Published sections, seats, and a conflict-free timetable are not confirmed."
            )
        checked_term = term
    if len(facts) > 12 or any(len(f) > 1000 for f in facts):
        return stop(
            "ABSTAIN",
            "Please narrow the question so the complete explanation fits the supported response budget.",
        )
    draft = "\n\n".join(facts)
    for source in evidence:
        candidate = "\n\n".join(s for s in (draft, source["quote"]) if s)
        if len(candidate) <= 6000:
            draft = candidate
    if not draft or len(draft) > 6000:
        return stop(
            "ABSTAIN",
            "The academic explanation exceeds the supported response budget. Please ask a narrower question.",
        )
    db.rollback()
    if not (await gateway.status())["available"]:
        return stop(
            "UNAVAILABLE", "AI advising is unavailable. Your degree audit and planner remain available."
        )
    envelope = dict(
        catalogVersion=version,
        question=PRIVATE.sub("[redacted]", request.question.strip()),
        intent=intent,
        facts=facts,
        evidence=evidence,
        drafts=[dict(answer=draft, citedSectionIds=[e["policySectionId"] for e in evidence])],
    )
    reply = await gateway.call("advisor/answer", envelope, timeout=25, limit=32768)
    if not reply:
        return stop("UNAVAILABLE", failure_message(None))
    cited, limits = reply.get("citedSectionIds"), reply.get("limitations")
    if (
        reply.get("status") != "ANSWERED"
        or reply.get("answer") != draft
        or not isinstance(cited, list)
        or not all(isinstance(i, str) for i in cited)
        or len(cited) != len(set(cited))
        or set(cited) != set(ids)
        or not isinstance(limits, list)
        or len(limits) > 2
        or any(limit not in (DISCLAIMER, LIMITED) for limit in limits)
    ):
        return stop(
            "UNAVAILABLE" if reply.get("status") == "UNAVAILABLE" else "ABSTAIN",
            failure_message(reply.get("reason")),
        )
    db.expire_all()
    student = db.get(m.Student, actor)
    changed = (
        not student
        or student.catalog_version != version
        or sources.read(db, version, ids) != evidence
        or not sources.rules_approved(db, version)
    )
    if audited is not None:
        current = degree_audit(db, actor)
        changed |= current is None or any(
            current[k] != audited[k]
            for k in (
                "status",
                "earnedCredits",
                "requiredCredits",
                "degreeComplete",
                "gpa",
                "buckets",
                "coreRequirements",
            )
        )
    if checked_term:
        current = load_context(db, actor, checked_term)
        changed |= current is None or bool(context_issues(current))
        if current:
            changed |= any(eligibility(current, r["courseCode"]) != r for r in checked_eligibility)
            changed |= checked_options is not None and options(current) != checked_options
    if changed:
        return stop(
            "ABSTAIN",
            "The academic facts or approved sources changed while preparing this reply. Ask again to use current information.",
        )
    return dict(
        status="ANSWERED",
        catalogVersion=version,
        answer=draft,
        citations=evidence,
        limitations=[DISCLAIMER, LIMITED],
        facts=facts,
    )
