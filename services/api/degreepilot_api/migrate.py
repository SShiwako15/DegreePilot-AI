from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import inspect, text

from .models import Base

LEGACY_REVISIONS = {
    "20261021033919_InitialAcademicSlice",
    "20261021125608_AddTermPlanning",
    "20261021144321_AddApprovedPolicies",
    "20261021161502_AddAdvisorAdministration",
}


def migrate(engine):
    config = Config(str(Path(__file__).resolve().parents[1] / "alembic.ini"))
    with engine.begin() as connection:
        if engine.dialect.name == "postgresql":
            # Serialize startup migrations across workers without exposing database credentials.
            connection.execute(text("SELECT pg_advisory_xact_lock(74912026)"))
        inspector = inspect(connection)
        tables = set(inspector.get_table_names())
        if "alembic_version" not in tables and "Users" in tables:
            if "__EFMigrationsHistory" not in tables:
                raise RuntimeError("Existing database has no recognized migration history.")
            revisions = set(
                connection.execute(text('SELECT "MigrationId" FROM "__EFMigrationsHistory"')).scalars()
            )
            if revisions != LEGACY_REVISIONS:
                raise RuntimeError("Existing database requires the complete known schema before migration.")
            for table in Base.metadata.sorted_tables:
                if table.name not in tables or {c.name for c in table.columns} != {
                    c["name"] for c in inspector.get_columns(table.name)
                }:
                    raise RuntimeError("Existing schema differs from the supported migration baseline.")
            config.attributes["connection"] = connection
            command.stamp(config, "0001")
        config.attributes["connection"] = connection
        command.upgrade(config, "head")


if __name__ == "__main__":
    from .database import make_engine
    from .settings import Settings

    migrate(make_engine(Settings().database))
