using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DegreePilot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvisorAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalHash",
                table: "PolicyDocuments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReplacesDocumentId",
                table: "PolicyDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "PolicyDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalHash",
                table: "Catalogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Catalogs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Catalogs",
                type: "text",
                nullable: false,
                defaultValue: "published");

            migrationBuilder.CreateTable(
                name: "AdvisorAssignments",
                columns: table => new
                {
                    AdvisorId = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvisorAssignments", x => new { x.AdvisorId, x.StudentId });
                    table.ForeignKey(
                        name: "FK_AdvisorAssignments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvisorAssignments_Users_AdvisorId",
                        column: x => x.AdvisorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AdvisorSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvisorId = table.Column<string>(type: "text", nullable: false),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    TermCode = table.Column<string>(type: "text", nullable: false),
                    ConstraintsJson = table.Column<string>(type: "text", nullable: false),
                    SelectionIdsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvisorSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvisorSuggestions_Students_StudentId_CatalogVersion",
                        columns: x => new { x.StudentId, x.CatalogVersion },
                        principalTable: "Students",
                        principalColumns: new[] { "UserId", "CatalogVersion" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvisorSuggestions_Terms_TermCode",
                        column: x => x.TermCode,
                        principalTable: "Terms",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AdvisorSuggestions_Users_AdvisorId",
                        column: x => x.AdvisorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MutationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MutationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MutationAudits_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocuments_ReplacesDocumentId",
                table: "PolicyDocuments",
                column: "ReplacesDocumentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Catalog_Status",
                table: "Catalogs",
                sql: "\"Status\" IN ('draft','published')");

            migrationBuilder.CreateIndex(
                name: "IX_AdvisorAssignments_StudentId",
                table: "AdvisorAssignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvisorSuggestions_AdvisorId",
                table: "AdvisorSuggestions",
                column: "AdvisorId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvisorSuggestions_StudentId_CatalogVersion",
                table: "AdvisorSuggestions",
                columns: new[] { "StudentId", "CatalogVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvisorSuggestions_TermCode",
                table: "AdvisorSuggestions",
                column: "TermCode");

            migrationBuilder.CreateIndex(
                name: "IX_MutationAudits_ActorId",
                table: "MutationAudits",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_MutationAudits_OccurredAt",
                table: "MutationAudits",
                column: "OccurredAt");

            migrationBuilder.AddForeignKey(
                name: "FK_PolicyDocuments_PolicyDocuments_ReplacesDocumentId",
                table: "PolicyDocuments",
                column: "ReplacesDocumentId",
                principalTable: "PolicyDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PolicyDocuments_PolicyDocuments_ReplacesDocumentId",
                table: "PolicyDocuments");

            migrationBuilder.DropTable(
                name: "AdvisorAssignments");

            migrationBuilder.DropTable(
                name: "AdvisorSuggestions");

            migrationBuilder.DropTable(
                name: "MutationAudits");

            migrationBuilder.DropIndex(
                name: "IX_PolicyDocuments_ReplacesDocumentId",
                table: "PolicyDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Catalog_Status",
                table: "Catalogs");

            migrationBuilder.DropColumn(
                name: "ApprovalHash",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "ReplacesDocumentId",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "PolicyDocuments");

            migrationBuilder.DropColumn(
                name: "ApprovalHash",
                table: "Catalogs");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Catalogs");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Catalogs");
        }
    }
}
