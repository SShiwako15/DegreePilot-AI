using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DegreePilot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTermPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Code);
                    table.CheckConstraint("CK_Term_Dates", "\"StartsOn\" < \"EndsOn\"");
                    table.CheckConstraint("CK_Term_Kind", "\"Kind\" IN ('Spring','Fall')");
                });

            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    TermCode = table.Column<string>(type: "text", nullable: false),
                    ConstraintsJson = table.Column<string>(type: "text", nullable: false),
                    SelectionIdsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Plans_Students_StudentId_CatalogVersion",
                        columns: x => new { x.StudentId, x.CatalogVersion },
                        principalTable: "Students",
                        principalColumns: new[] { "UserId", "CatalogVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Plans_Terms_TermCode",
                        column: x => x.TermCode,
                        principalTable: "Terms",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    TermCode = table.Column<string>(type: "text", nullable: false),
                    CourseCode = table.Column<string>(type: "text", nullable: false),
                    SectionCode = table.Column<string>(type: "text", nullable: false),
                    DaysJson = table.Column<string>(type: "text", nullable: false),
                    StartLocal = table.Column<string>(type: "text", nullable: false),
                    EndLocal = table.Column<string>(type: "text", nullable: false),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    AvailableSeats = table.Column<int>(type: "integer", nullable: false),
                    Delivery = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => new { x.CatalogVersion, x.Id });
                    table.CheckConstraint("CK_Section_Seats", "\"Capacity\" > 0 AND \"AvailableSeats\" >= 0 AND \"AvailableSeats\" <= \"Capacity\"");
                    table.CheckConstraint("CK_Section_Times", "\"StartLocal\" < \"EndLocal\"");
                    table.ForeignKey(
                        name: "FK_Sections_Courses_CatalogVersion_CourseCode",
                        columns: x => new { x.CatalogVersion, x.CourseCode },
                        principalTable: "Courses",
                        principalColumns: new[] { "CatalogVersion", "Code" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sections_Terms_TermCode",
                        column: x => x.TermCode,
                        principalTable: "Terms",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_StudentId_CatalogVersion",
                table: "Plans",
                columns: new[] { "StudentId", "CatalogVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_TermCode",
                table: "Plans",
                column: "TermCode");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_CatalogVersion_CourseCode",
                table: "Sections",
                columns: new[] { "CatalogVersion", "CourseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Sections_CatalogVersion_TermCode_SectionCode",
                table: "Sections",
                columns: new[] { "CatalogVersion", "TermCode", "SectionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sections_TermCode",
                table: "Sections",
                column: "TermCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Plans");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Terms");
        }
    }
}
