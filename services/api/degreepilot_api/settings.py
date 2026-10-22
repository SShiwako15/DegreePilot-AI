import os
import secrets
from dataclasses import dataclass, field
from pathlib import Path

from sqlalchemy.engine import URL

ROOT = Path(__file__).resolve().parents[3]


def configured_database() -> str:
    explicit = os.getenv("DATABASE_URL")
    if explicit:
        return database_url(explicit)
    return URL.create(
        "postgresql+psycopg",
        username=os.getenv("DB_USER", "degreepilot"),
        password=os.getenv("DB_PASSWORD", "local-fictional-database-only"),
        host=os.getenv("DB_HOST", "127.0.0.1"),
        port=int(os.getenv("DB_PORT", "5432")),
        database=os.getenv("DB_NAME", "degreepilot"),
    ).render_as_string(hide_password=False)


def database_url(value: str) -> str:
    """Accept the existing local connection setting during migration."""
    if "://" in value:
        return value.replace("postgresql://", "postgresql+psycopg://", 1)
    parts = dict(p.split("=", 1) for p in value.strip(";").split(";"))
    parts = {k.lower(): v.strip('"') for k, v in parts.items()}
    return URL.create(
        "postgresql+psycopg",
        username=parts.get("username"),
        password=parts.get("password"),
        host=parts.get("host", "localhost"),
        port=int(parts.get("port", "5432")),
        database=parts.get("database", "degreepilot"),
    ).render_as_string(hide_password=False)


@dataclass(frozen=True)
class Settings:
    environment: str = field(default_factory=lambda: os.getenv("APP_ENV", "Development"))
    demo_enabled: bool = field(default_factory=lambda: os.getenv("DEMO_ENABLED", "false").lower() == "true")
    demo_password: str = field(default_factory=lambda: os.getenv("DEMO_PASSWORD", ""), repr=False)
    database: str = field(default_factory=configured_database, repr=False)
    planner_url: str = field(default_factory=lambda: os.getenv("PLANNER_URL", "http://127.0.0.1:8000/"))
    session_secret: str = field(default_factory=lambda: os.getenv("SESSION_SECRET", ""), repr=False)
    login_limit: int = field(default_factory=lambda: int(os.getenv("Security__LoginRequestsPerMinute", "20")))
    planner_limit: int = field(
        default_factory=lambda: int(os.getenv("Security__PlannerRequestsPerMinute", "12"))
    )
    advisor_limit: int = field(default_factory=lambda: int(os.getenv("Advisor__RequestsPerMinute", "5")))

    @property
    def local(self) -> bool:
        return self.environment in {"Development", "Testing"}

    def signing_key(self) -> str:
        if self.demo_enabled and not self.local:
            raise RuntimeError("Demo accounts are restricted to Development and Testing.")
        if self.session_secret:
            if len(self.session_secret) < 32:
                raise RuntimeError("SESSION_SECRET must contain at least 32 characters.")
            return self.session_secret
        if not self.local:
            raise RuntimeError("SESSION_SECRET is required outside local development.")
        return secrets.token_urlsafe(48)
