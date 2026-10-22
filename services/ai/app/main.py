from fastapi import Depends, FastAPI
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .advisor import AdvisorReply, AdvisorRequest, OpenAIProvider, answer, availability, get_provider
from .degree_models import DegreeReply, DegreeRequest
from .degree_solver import solve_degree
from .models import SolveRequest, SolveResult
from .solver import solve

# No transcripts, identities, database access, LLM client, or public host port.
app = FastAPI(title="DegreePilot internal deterministic planner", docs_url=None, redoc_url=None)


@app.exception_handler(RequestValidationError)
async def invalid_request(_request, _error):
    return JSONResponse(status_code=422, content={"detail": "Invalid bounded internal request."})


@app.get("/advisor/status")
def advisor_status():
    return availability()


@app.post("/advisor/answer", response_model=AdvisorReply)
async def advisor_answer(request: AdvisorRequest, provider: OpenAIProvider = Depends(get_provider)):
    return await answer(request, provider)


@app.get("/health")
def health():
    return {"status": "healthy"}


@app.post("/solve", response_model=SolveResult)
def generate(request: SolveRequest):
    return solve(request)


@app.post("/degree/solve", response_model=DegreeReply)
def generate_degree(request: DegreeRequest):
    return solve_degree(request)
