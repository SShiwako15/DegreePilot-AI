CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;
CREATE TABLE "Catalogs" (
    "Id" text NOT NULL,
    "ProgramId" text NOT NULL,
    "RulesJson" text NOT NULL,
    "SourceHash" text NOT NULL,
    CONSTRAINT "PK_Catalogs" PRIMARY KEY ("Id")
);

CREATE TABLE "Users" (
    "Id" text NOT NULL,
    "DisplayName" text NOT NULL,
    "Email" text NOT NULL,
    "Role" text NOT NULL,
    "PasswordHash" text NOT NULL,
    CONSTRAINT "PK_Users" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_User_Role" CHECK ("Role" IN ('student','advisor','admin'))
);

CREATE TABLE "Courses" (
    "CatalogVersion" text NOT NULL,
    "Code" text NOT NULL,
    "Title" text NOT NULL,
    "Credits" integer NOT NULL,
    "Category" text NOT NULL,
    "PrerequisitesJson" text NOT NULL,
    "OfferedTermsJson" text NOT NULL,
    CONSTRAINT "PK_Courses" PRIMARY KEY ("CatalogVersion", "Code"),
    CONSTRAINT "CK_Course_Category" CHECK ("Category" IN ('core','cs_elective','gen_ed','free_elective')),
    CONSTRAINT "CK_Course_Credits" CHECK ("Credits" > 0 AND "Credits" <= 30),
    CONSTRAINT "FK_Courses_Catalogs_CatalogVersion" FOREIGN KEY ("CatalogVersion") REFERENCES "Catalogs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Students" (
    "UserId" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    CONSTRAINT "PK_Students" PRIMARY KEY ("UserId"),
    CONSTRAINT "AK_Students_UserId_CatalogVersion" UNIQUE ("UserId", "CatalogVersion"),
    CONSTRAINT "FK_Students_Catalogs_CatalogVersion" FOREIGN KEY ("CatalogVersion") REFERENCES "Catalogs" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_Students_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Attempts" (
    "Id" uuid NOT NULL,
    "StudentId" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    "CourseCode" text NOT NULL,
    "Term" text NOT NULL,
    "Grade" text NOT NULL,
    CONSTRAINT "PK_Attempts" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Attempt_Grade" CHECK ("Grade" IN ('A','B','C','D','F','W','I','P')),
    CONSTRAINT "FK_Attempts_Courses_CatalogVersion_CourseCode" FOREIGN KEY ("CatalogVersion", "CourseCode") REFERENCES "Courses" ("CatalogVersion", "Code") ON DELETE RESTRICT,
    CONSTRAINT "FK_Attempts_Students_StudentId_CatalogVersion" FOREIGN KEY ("StudentId", "CatalogVersion") REFERENCES "Students" ("UserId", "CatalogVersion") ON DELETE RESTRICT
);

CREATE INDEX "IX_Attempts_CatalogVersion_CourseCode" ON "Attempts" ("CatalogVersion", "CourseCode");

CREATE INDEX "IX_Attempts_StudentId_CatalogVersion" ON "Attempts" ("StudentId", "CatalogVersion");

CREATE UNIQUE INDEX "IX_Attempts_StudentId_CourseCode_Term" ON "Attempts" ("StudentId", "CourseCode", "Term");

CREATE INDEX "IX_Students_CatalogVersion" ON "Students" ("CatalogVersion");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021033919_InitialAcademicSlice', '10.0.4');

COMMIT;

START TRANSACTION;
CREATE TABLE "Terms" (
    "Code" text NOT NULL,
    "Name" text NOT NULL,
    "Kind" text NOT NULL,
    "StartsOn" date NOT NULL,
    "EndsOn" date NOT NULL,
    "Timezone" text NOT NULL,
    "IsPublished" boolean NOT NULL,
    CONSTRAINT "PK_Terms" PRIMARY KEY ("Code"),
    CONSTRAINT "CK_Term_Dates" CHECK ("StartsOn" < "EndsOn"),
    CONSTRAINT "CK_Term_Kind" CHECK ("Kind" IN ('Spring','Fall'))
);

CREATE TABLE "Plans" (
    "Id" uuid NOT NULL,
    "StudentId" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    "TermCode" text NOT NULL,
    "ConstraintsJson" text NOT NULL,
    "SelectionIdsJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Plans" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Plans_Students_StudentId_CatalogVersion" FOREIGN KEY ("StudentId", "CatalogVersion") REFERENCES "Students" ("UserId", "CatalogVersion") ON DELETE RESTRICT,
    CONSTRAINT "FK_Plans_Terms_TermCode" FOREIGN KEY ("TermCode") REFERENCES "Terms" ("Code") ON DELETE RESTRICT
);

CREATE TABLE "Sections" (
    "Id" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    "TermCode" text NOT NULL,
    "CourseCode" text NOT NULL,
    "SectionCode" text NOT NULL,
    "DaysJson" text NOT NULL,
    "StartLocal" text NOT NULL,
    "EndLocal" text NOT NULL,
    "Timezone" text NOT NULL,
    "Capacity" integer NOT NULL,
    "AvailableSeats" integer NOT NULL,
    "Delivery" text NOT NULL,
    CONSTRAINT "PK_Sections" PRIMARY KEY ("CatalogVersion", "Id"),
    CONSTRAINT "CK_Section_Seats" CHECK ("Capacity" > 0 AND "AvailableSeats" >= 0 AND "AvailableSeats" <= "Capacity"),
    CONSTRAINT "CK_Section_Times" CHECK ("StartLocal" < "EndLocal"),
    CONSTRAINT "FK_Sections_Courses_CatalogVersion_CourseCode" FOREIGN KEY ("CatalogVersion", "CourseCode") REFERENCES "Courses" ("CatalogVersion", "Code") ON DELETE RESTRICT,
    CONSTRAINT "FK_Sections_Terms_TermCode" FOREIGN KEY ("TermCode") REFERENCES "Terms" ("Code") ON DELETE RESTRICT
);

CREATE INDEX "IX_Plans_StudentId_CatalogVersion" ON "Plans" ("StudentId", "CatalogVersion");

CREATE INDEX "IX_Plans_TermCode" ON "Plans" ("TermCode");

CREATE INDEX "IX_Sections_CatalogVersion_CourseCode" ON "Sections" ("CatalogVersion", "CourseCode");

CREATE UNIQUE INDEX "IX_Sections_CatalogVersion_TermCode_SectionCode" ON "Sections" ("CatalogVersion", "TermCode", "SectionCode");

CREATE INDEX "IX_Sections_TermCode" ON "Sections" ("TermCode");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021125608_AddTermPlanning', '10.0.4');

COMMIT;

START TRANSACTION;
CREATE TABLE "PolicyDocuments" (
    "Id" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    "Title" text NOT NULL,
    "Version" integer NOT NULL,
    "Status" text NOT NULL,
    "SourceHash" text NOT NULL,
    CONSTRAINT "PK_PolicyDocuments" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_Policy_Status" CHECK ("Status" IN ('draft','approved','retired')),
    CONSTRAINT "CK_Policy_Version" CHECK ("Version" > 0),
    CONSTRAINT "FK_PolicyDocuments_Catalogs_CatalogVersion" FOREIGN KEY ("CatalogVersion") REFERENCES "Catalogs" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PolicySections" (
    "DocumentId" text NOT NULL,
    "Id" text NOT NULL,
    "Title" text NOT NULL,
    "Text" text NOT NULL,
    CONSTRAINT "PK_PolicySections" PRIMARY KEY ("DocumentId", "Id"),
    CONSTRAINT "FK_PolicySections_PolicyDocuments_DocumentId" FOREIGN KEY ("DocumentId") REFERENCES "PolicyDocuments" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_PolicyDocuments_CatalogVersion" ON "PolicyDocuments" ("CatalogVersion");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021144321_AddApprovedPolicies', '10.0.4');

COMMIT;

START TRANSACTION;
ALTER TABLE "PolicyDocuments" ADD "ApprovalHash" text NOT NULL DEFAULT '';

ALTER TABLE "PolicyDocuments" ADD "ReplacesDocumentId" text;

ALTER TABLE "PolicyDocuments" ADD "Revision" integer NOT NULL DEFAULT 1;

ALTER TABLE "Catalogs" ADD "ApprovalHash" text NOT NULL DEFAULT '';

ALTER TABLE "Catalogs" ADD "Revision" integer NOT NULL DEFAULT 1;

ALTER TABLE "Catalogs" ADD "Status" text NOT NULL DEFAULT 'published';

CREATE TABLE "AdvisorAssignments" (
    "AdvisorId" text NOT NULL,
    "StudentId" text NOT NULL,
    "Active" boolean NOT NULL,
    "Revision" integer NOT NULL,
    CONSTRAINT "PK_AdvisorAssignments" PRIMARY KEY ("AdvisorId", "StudentId"),
    CONSTRAINT "FK_AdvisorAssignments_Students_StudentId" FOREIGN KEY ("StudentId") REFERENCES "Students" ("UserId") ON DELETE RESTRICT,
    CONSTRAINT "FK_AdvisorAssignments_Users_AdvisorId" FOREIGN KEY ("AdvisorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "AdvisorSuggestions" (
    "Id" uuid NOT NULL,
    "AdvisorId" text NOT NULL,
    "StudentId" text NOT NULL,
    "CatalogVersion" text NOT NULL,
    "TermCode" text NOT NULL,
    "ConstraintsJson" text NOT NULL,
    "SelectionIdsJson" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AdvisorSuggestions" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AdvisorSuggestions_Students_StudentId_CatalogVersion" FOREIGN KEY ("StudentId", "CatalogVersion") REFERENCES "Students" ("UserId", "CatalogVersion") ON DELETE RESTRICT,
    CONSTRAINT "FK_AdvisorSuggestions_Terms_TermCode" FOREIGN KEY ("TermCode") REFERENCES "Terms" ("Code") ON DELETE RESTRICT,
    CONSTRAINT "FK_AdvisorSuggestions_Users_AdvisorId" FOREIGN KEY ("AdvisorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "MutationAudits" (
    "Id" uuid NOT NULL,
    "ActorId" text NOT NULL,
    "Action" text NOT NULL,
    "ResourceId" text NOT NULL,
    "Revision" integer NOT NULL,
    "ContentHash" text NOT NULL,
    "OccurredAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MutationAudits" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_MutationAudits_Users_ActorId" FOREIGN KEY ("ActorId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_PolicyDocuments_ReplacesDocumentId" ON "PolicyDocuments" ("ReplacesDocumentId");

ALTER TABLE "Catalogs" ADD CONSTRAINT "CK_Catalog_Status" CHECK ("Status" IN ('draft','published'));

CREATE INDEX "IX_AdvisorAssignments_StudentId" ON "AdvisorAssignments" ("StudentId");

CREATE INDEX "IX_AdvisorSuggestions_AdvisorId" ON "AdvisorSuggestions" ("AdvisorId");

CREATE INDEX "IX_AdvisorSuggestions_StudentId_CatalogVersion" ON "AdvisorSuggestions" ("StudentId", "CatalogVersion");

CREATE INDEX "IX_AdvisorSuggestions_TermCode" ON "AdvisorSuggestions" ("TermCode");

CREATE INDEX "IX_MutationAudits_ActorId" ON "MutationAudits" ("ActorId");

CREATE INDEX "IX_MutationAudits_OccurredAt" ON "MutationAudits" ("OccurredAt");

ALTER TABLE "PolicyDocuments" ADD CONSTRAINT "FK_PolicyDocuments_PolicyDocuments_ReplacesDocumentId" FOREIGN KEY ("ReplacesDocumentId") REFERENCES "PolicyDocuments" ("Id") ON DELETE RESTRICT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261021161502_AddAdvisorAdministration', '10.0.4');

COMMIT;
