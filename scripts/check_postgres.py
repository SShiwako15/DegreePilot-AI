"""Run disposable PostgreSQL integration checks without printing local connection settings."""

import json
import os
import subprocess
import sys
from pathlib import Path

from sqlalchemy.engine import URL

ROOT = Path(__file__).resolve().parent.parent


def main():
    if not os.getenv("DEGREEPILOT_TEST_DATABASE"):
        try:
            result = subprocess.run(
                ["docker", "compose", "config", "--format", "json"],
                cwd=ROOT,
                capture_output=True,
                text=True,
                check=True,
            )
            config = json.loads(result.stdout)["services"]["postgres"]["environment"]
            os.environ["DEGREEPILOT_TEST_DATABASE"] = URL.create(
                "postgresql+psycopg",
                host="127.0.0.1",
                port=5432,
                username=config["POSTGRES_USER"],
                password=config["POSTGRES_PASSWORD"],
                database=config["POSTGRES_DB"],
            ).render_as_string(hide_password=False)
        except (subprocess.SubprocessError, ValueError, KeyError):
            print(
                "Could not resolve local Compose database settings. Start PostgreSQL or set DEGREEPILOT_TEST_DATABASE."
            )
            return 1
    import pytest

    return pytest.main([str(ROOT / "tests/api/test_postgres.py"), "-q", "-c", str(ROOT / "pyproject.toml")])


if __name__ == "__main__":
    sys.exit(main())
