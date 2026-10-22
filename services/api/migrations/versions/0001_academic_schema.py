"""Academic schema baseline; existing supported databases are adopted without rewriting records."""

import sqlalchemy as sa
from alembic import op

revision = "0001"
down_revision = None
branch_labels = None
depends_on = None


def upgrade():
    op.create_table(
        "Catalogs",
        sa.Column("Id", sa.Text(), nullable=False),
        sa.Column("ProgramId", sa.Text(), nullable=False),
        sa.Column("RulesJson", sa.Text(), nullable=False),
        sa.Column("SourceHash", sa.Text(), nullable=False),
        sa.Column("Status", sa.Text(), nullable=False),
        sa.Column("Revision", sa.Integer(), nullable=False),
        sa.Column("ApprovalHash", sa.Text(), nullable=False),
        sa.CheckConstraint("\"Status\" IN ('draft','published')", name="CK_Catalog_Status"),
        sa.PrimaryKeyConstraint("Id"),
    )
    op.create_table(
        "Terms",
        sa.Column("Code", sa.Text(), nullable=False),
        sa.Column("Name", sa.Text(), nullable=False),
        sa.Column("Kind", sa.Text(), nullable=False),
        sa.Column("StartsOn", sa.Date(), nullable=False),
        sa.Column("EndsOn", sa.Date(), nullable=False),
        sa.Column("Timezone", sa.Text(), nullable=False),
        sa.Column("IsPublished", sa.Boolean(), nullable=False),
        sa.CheckConstraint("\"Kind\" IN ('Spring','Fall')", name="CK_Term_Kind"),
        sa.CheckConstraint('"StartsOn" < "EndsOn"', name="CK_Term_Dates"),
        sa.PrimaryKeyConstraint("Code"),
    )
    op.create_table(
        "Users",
        sa.Column("Id", sa.Text(), nullable=False),
        sa.Column("DisplayName", sa.Text(), nullable=False),
        sa.Column("Email", sa.Text(), nullable=False),
        sa.Column("Role", sa.Text(), nullable=False),
        sa.Column("PasswordHash", sa.Text(), nullable=False),
        sa.CheckConstraint("\"Role\" IN ('student','advisor','admin')", name="CK_User_Role"),
        sa.PrimaryKeyConstraint("Id"),
        sa.UniqueConstraint("Email"),
    )
    op.create_table(
        "Courses",
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("Code", sa.Text(), nullable=False),
        sa.Column("Title", sa.Text(), nullable=False),
        sa.Column("Credits", sa.Integer(), nullable=False),
        sa.Column("Category", sa.Text(), nullable=False),
        sa.Column("PrerequisitesJson", sa.Text(), nullable=False),
        sa.Column("OfferedTermsJson", sa.Text(), nullable=False),
        sa.CheckConstraint(
            "\"Category\" IN ('core','cs_elective','gen_ed','free_elective')", name="CK_Course_Category"
        ),
        sa.CheckConstraint('"Credits" > 0 AND "Credits" <= 30', name="CK_Course_Credits"),
        sa.ForeignKeyConstraint(
            ["CatalogVersion"],
            ["Catalogs.Id"],
        ),
        sa.PrimaryKeyConstraint("CatalogVersion", "Code"),
    )
    op.create_table(
        "MutationAudits",
        sa.Column("Id", sa.Uuid(), nullable=False),
        sa.Column("ActorId", sa.Text(), nullable=False),
        sa.Column("Action", sa.Text(), nullable=False),
        sa.Column("ResourceId", sa.Text(), nullable=False),
        sa.Column("Revision", sa.Integer(), nullable=False),
        sa.Column("ContentHash", sa.Text(), nullable=False),
        sa.Column("OccurredAt", sa.DateTime(timezone=True), nullable=False),
        sa.ForeignKeyConstraint(
            ["ActorId"],
            ["Users.Id"],
        ),
        sa.PrimaryKeyConstraint("Id"),
    )
    op.create_index("IX_MutationAudits_OccurredAt", "MutationAudits", ["OccurredAt"], unique=False)
    op.create_table(
        "PolicyDocuments",
        sa.Column("Id", sa.Text(), nullable=False),
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("Title", sa.Text(), nullable=False),
        sa.Column("Version", sa.Integer(), nullable=False),
        sa.Column("Status", sa.Text(), nullable=False),
        sa.Column("SourceHash", sa.Text(), nullable=False),
        sa.Column("ReplacesDocumentId", sa.Text(), nullable=True),
        sa.Column("ApprovalHash", sa.Text(), nullable=False),
        sa.Column("Revision", sa.Integer(), nullable=False),
        sa.CheckConstraint("\"Status\" IN ('draft','approved','retired')", name="CK_Policy_Status"),
        sa.CheckConstraint('"Version" > 0', name="CK_Policy_Version"),
        sa.ForeignKeyConstraint(
            ["CatalogVersion"],
            ["Catalogs.Id"],
        ),
        sa.ForeignKeyConstraint(
            ["ReplacesDocumentId"],
            ["PolicyDocuments.Id"],
        ),
        sa.PrimaryKeyConstraint("Id"),
    )
    op.create_table(
        "Students",
        sa.Column("UserId", sa.Text(), nullable=False),
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.ForeignKeyConstraint(
            ["CatalogVersion"],
            ["Catalogs.Id"],
        ),
        sa.ForeignKeyConstraint(
            ["UserId"],
            ["Users.Id"],
        ),
        sa.PrimaryKeyConstraint("UserId"),
        sa.UniqueConstraint("UserId", "CatalogVersion"),
    )
    op.create_table(
        "AdvisorAssignments",
        sa.Column("AdvisorId", sa.Text(), nullable=False),
        sa.Column("StudentId", sa.Text(), nullable=False),
        sa.Column("Active", sa.Boolean(), nullable=False),
        sa.Column("Revision", sa.Integer(), nullable=False),
        sa.ForeignKeyConstraint(
            ["AdvisorId"],
            ["Users.Id"],
        ),
        sa.ForeignKeyConstraint(
            ["StudentId"],
            ["Students.UserId"],
        ),
        sa.PrimaryKeyConstraint("AdvisorId", "StudentId"),
    )
    op.create_table(
        "AdvisorSuggestions",
        sa.Column("Id", sa.Uuid(), nullable=False),
        sa.Column("AdvisorId", sa.Text(), nullable=False),
        sa.Column("StudentId", sa.Text(), nullable=False),
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("TermCode", sa.Text(), nullable=False),
        sa.Column("ConstraintsJson", sa.Text(), nullable=False),
        sa.Column("SelectionIdsJson", sa.Text(), nullable=False),
        sa.Column("CreatedAt", sa.DateTime(timezone=True), nullable=False),
        sa.ForeignKeyConstraint(
            ["AdvisorId"],
            ["Users.Id"],
        ),
        sa.ForeignKeyConstraint(
            ["StudentId", "CatalogVersion"],
            ["Students.UserId", "Students.CatalogVersion"],
        ),
        sa.ForeignKeyConstraint(
            ["TermCode"],
            ["Terms.Code"],
        ),
        sa.PrimaryKeyConstraint("Id"),
    )
    op.create_table(
        "Attempts",
        sa.Column("Id", sa.Uuid(), nullable=False),
        sa.Column("StudentId", sa.Text(), nullable=False),
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("CourseCode", sa.Text(), nullable=False),
        sa.Column("Term", sa.Text(), nullable=False),
        sa.Column("Grade", sa.Text(), nullable=False),
        sa.CheckConstraint("\"Grade\" IN ('A','B','C','D','F','W','I','P')", name="CK_Attempt_Grade"),
        sa.ForeignKeyConstraint(
            ["CatalogVersion", "CourseCode"],
            ["Courses.CatalogVersion", "Courses.Code"],
        ),
        sa.ForeignKeyConstraint(
            ["StudentId", "CatalogVersion"],
            ["Students.UserId", "Students.CatalogVersion"],
        ),
        sa.PrimaryKeyConstraint("Id"),
        sa.UniqueConstraint("StudentId", "CourseCode", "Term"),
    )
    op.create_table(
        "Plans",
        sa.Column("Id", sa.Uuid(), nullable=False),
        sa.Column("StudentId", sa.Text(), nullable=False),
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("TermCode", sa.Text(), nullable=False),
        sa.Column("ConstraintsJson", sa.Text(), nullable=False),
        sa.Column("SelectionIdsJson", sa.Text(), nullable=False),
        sa.Column("CreatedAt", sa.DateTime(timezone=True), nullable=False),
        sa.ForeignKeyConstraint(
            ["StudentId", "CatalogVersion"],
            ["Students.UserId", "Students.CatalogVersion"],
        ),
        sa.ForeignKeyConstraint(
            ["TermCode"],
            ["Terms.Code"],
        ),
        sa.PrimaryKeyConstraint("Id"),
    )
    op.create_table(
        "PolicySections",
        sa.Column("DocumentId", sa.Text(), nullable=False),
        sa.Column("Id", sa.Text(), nullable=False),
        sa.Column("Title", sa.Text(), nullable=False),
        sa.Column("Text", sa.Text(), nullable=False),
        sa.ForeignKeyConstraint(
            ["DocumentId"],
            ["PolicyDocuments.Id"],
        ),
        sa.PrimaryKeyConstraint("DocumentId", "Id"),
    )
    op.create_table(
        "Sections",
        sa.Column("CatalogVersion", sa.Text(), nullable=False),
        sa.Column("Id", sa.Text(), nullable=False),
        sa.Column("TermCode", sa.Text(), nullable=False),
        sa.Column("CourseCode", sa.Text(), nullable=False),
        sa.Column("SectionCode", sa.Text(), nullable=False),
        sa.Column("DaysJson", sa.Text(), nullable=False),
        sa.Column("StartLocal", sa.Text(), nullable=False),
        sa.Column("EndLocal", sa.Text(), nullable=False),
        sa.Column("Timezone", sa.Text(), nullable=False),
        sa.Column("Capacity", sa.Integer(), nullable=False),
        sa.Column("AvailableSeats", sa.Integer(), nullable=False),
        sa.Column("Delivery", sa.Text(), nullable=False),
        sa.CheckConstraint(
            '"Capacity" > 0 AND "AvailableSeats" >= 0 AND "AvailableSeats" <= "Capacity"',
            name="CK_Section_Seats",
        ),
        sa.CheckConstraint('"StartLocal" < "EndLocal"', name="CK_Section_Times"),
        sa.ForeignKeyConstraint(
            ["CatalogVersion", "CourseCode"],
            ["Courses.CatalogVersion", "Courses.Code"],
        ),
        sa.ForeignKeyConstraint(
            ["TermCode"],
            ["Terms.Code"],
        ),
        sa.PrimaryKeyConstraint("CatalogVersion", "Id"),
        sa.UniqueConstraint("CatalogVersion", "TermCode", "SectionCode"),
    )


def downgrade():
    raise RuntimeError("Baseline downgrade would destroy academic records; restore a backup instead.")
