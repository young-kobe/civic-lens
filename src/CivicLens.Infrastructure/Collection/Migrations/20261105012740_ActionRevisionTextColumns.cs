using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionTextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "text_package_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "why_it_matters",
                table: "action_revisions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "action_revision_text_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_text_citations", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_text_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_text_citations_part", "part IN ('Summary', 'WhyItMatters')");
                    table.CheckConstraint("ck_action_revision_text_citations_position", "position BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_action_revision_text_citations_quote", "citation_length <= 1000");
                    table.ForeignKey(
                        name: "FK_action_revision_text_citations_action_revisions_record_id_r~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_text_citations_document_extractions_citatio~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_congress_bill_type_bill_number_text_packag~",
                table: "action_revisions",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_text_source",
                table: "action_revisions",
                sql: "text_package_id IS NULL OR summary_citation_extraction_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_why_it_matters",
                table: "action_revisions",
                sql: "(text_package_id IS NULL) = (why_it_matters IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_action_revision_text_citations_citation_extraction_id",
                table: "action_revision_text_citations",
                column: "citation_extraction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_bill_text_versions_congress_bill_type_bill~",
                table: "action_revisions",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" },
                principalTable: "bill_text_versions",
                principalColumns: new[] { "congress", "bill_type", "bill_number", "package_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_bill_text_versions_congress_bill_type_bill~",
                table: "action_revisions");

            migrationBuilder.DropTable(
                name: "action_revision_text_citations");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_congress_bill_type_bill_number_text_packag~",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_text_source",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_why_it_matters",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "text_package_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "why_it_matters",
                table: "action_revisions");
        }
    }
}
