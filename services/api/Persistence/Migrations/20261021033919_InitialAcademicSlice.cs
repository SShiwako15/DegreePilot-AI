using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DegreePilot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAcademicSlice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ProgramId = table.Column<string>(type: "text", nullable: false),
                    RulesJson = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_User_Role", "\"Role\" IN ('student','advisor','admin')");
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Credits = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    PrerequisitesJson = table.Column<string>(type: "text", nullable: false),
                    OfferedTermsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => new { x.CatalogVersion, x.Code });
                    table.CheckConstraint("CK_Course_Category", "\"Category\" IN ('core','cs_elective','gen_ed','free_elective')");
                    table.CheckConstraint("CK_Course_Credits", "\"Credits\" > 0 AND \"Credits\" <= 30");
                    table.ForeignKey(
                        name: "FK_Courses_Catalogs_CatalogVersion",
                        column: x => x.CatalogVersion,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Students",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Students", x => x.UserId);
                    table.UniqueConstraint("AK_Students_UserId_CatalogVersion", x => new { x.UserId, x.CatalogVersion });
                    table.ForeignKey(
                        name: "FK_Students_Catalogs_CatalogVersion",
                        column: x => x.CatalogVersion,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Students_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    CourseCode = table.Column<string>(type: "text", nullable: false),
                    Term = table.Column<string>(type: "text", nullable: false),
                    Grade = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attempts", x => x.Id);
                    table.CheckConstraint("CK_Attempt_Grade", "\"Grade\" IN ('A','B','C','D','F','W','I','P')");
                    table.ForeignKey(
                        name: "FK_Attempts_Courses_CatalogVersion_CourseCode",
                        columns: x => new { x.CatalogVersion, x.CourseCode },
                        principalTable: "Courses",
                        principalColumns: new[] { "CatalogVersion", "Code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attempts_Students_StudentId_CatalogVersion",
                        columns: x => new { x.StudentId, x.CatalogVersion },
                        principalTable: "Students",
                        principalColumns: new[] { "UserId", "CatalogVersion" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_CatalogVersion_CourseCode",
                table: "Attempts",
                columns: new[] { "CatalogVersion", "CourseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_StudentId_CatalogVersion",
                table: "Attempts",
                columns: new[] { "StudentId", "CatalogVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_StudentId_CourseCode_Term",
                table: "Attempts",
                columns: new[] { "StudentId", "CourseCode", "Term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_CatalogVersion",
                table: "Students",
                column: "CatalogVersion");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attempts");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropTable(
                name: "Students");

            migrationBuilder.DropTable(
                name: "Catalogs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
