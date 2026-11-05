using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DocumentChangeRunRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_draft",
                table: "document_change_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_draft",
                table: "document_change_analysis_runs",
                sql: "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number >= 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_draft",
                table: "document_change_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_draft",
                table: "document_change_analysis_runs",
                sql: "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number = 1)");
        }
    }
}
