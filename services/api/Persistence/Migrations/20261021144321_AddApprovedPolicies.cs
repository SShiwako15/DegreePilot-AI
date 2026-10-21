using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DegreePilot.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovedPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PolicyDocuments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CatalogVersion = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicyDocuments", x => x.Id);
                    table.CheckConstraint("CK_Policy_Status", "\"Status\" IN ('draft','approved','retired')");
                    table.CheckConstraint("CK_Policy_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_PolicyDocuments_Catalogs_CatalogVersion",
                        column: x => x.CatalogVersion,
                        principalTable: "Catalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PolicySections",
                columns: table => new
                {
                    DocumentId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolicySections", x => new { x.DocumentId, x.Id });
                    table.ForeignKey(
                        name: "FK_PolicySections_PolicyDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "PolicyDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PolicyDocuments_CatalogVersion",
                table: "PolicyDocuments",
                column: "CatalogVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PolicySections");

            migrationBuilder.DropTable(
                name: "PolicyDocuments");
        }
    }
}
