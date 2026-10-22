"""Portable local checks; provider calls are disabled throughout verification."""

import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def run(*args):
    print("Running: " + " ".join(str(a) for a in args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def main():
    os.environ["AI_ADVISOR_ENABLED"] = "false"
    os.environ["OPENAI_API_KEY"] = ""
    api_python = ROOT / "services/api/.venv" / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    ai_python = ROOT / "services/ai/.venv" / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    if not api_python.is_file() or not ai_python.is_file():
        print("Create both API and planner virtual environments using README setup instructions.")
        return 1
    npm = "npm.cmd" if os.name == "nt" else "npm"
    try:
        run(str(api_python), "-m", "pytest", "-q", "tests/api")
        run(str(ai_python), "-m", "pytest", "-q", "-c", "services/ai/pyproject.toml", "services/ai/tests")
        paths = ["services/api/degreepilot_api", "services/api/migrations", "tests/api", "scripts"]
        run(str(api_python), "-m", "ruff", "check", *paths)
        run(str(api_python), "-m", "ruff", "format", "--check", *paths)
        run(
            str(ai_python),
            "-m",
            "ruff",
            "check",
            "--config",
            "services/ai/pyproject.toml",
            "services/ai/app",
            "services/ai/tests",
        )
        run(
            str(ai_python),
            "-m",
            "ruff",
            "format",
            "--check",
            "--config",
            "services/ai/pyproject.toml",
            "services/ai/app",
            "services/ai/tests",
        )
        for task in ("lint", "test", "build"):
            run(npm, "--prefix", "apps/web", "run", task)
        run(str(api_python), "scripts/security_check.py")
    except subprocess.CalledProcessError:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
