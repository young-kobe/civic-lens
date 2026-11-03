using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DocumentChangeRevisionColumns : Migration
    {
        private const string CheckSql = """
            DO $check$
            DECLARE mismatch text;
            BEGIN
                SELECT r.draft_id || ' ' || r.revision_number INTO mismatch FROM document_change_revisions r
                WHERE NOT pg_input_is_valid(r.revision_json, 'jsonb')
                ORDER BY r.draft_id, r.revision_number LIMIT 1;
                IF mismatch IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot set the author: revision % is not valid JSON', mismatch;
                END IF;
                SELECT r.draft_id || ' ' || r.revision_number INTO mismatch FROM document_change_revisions r
                WHERE jsonb_typeof(r.revision_json::jsonb -> 'authorSubject') IS DISTINCT FROM 'string'
                    OR length(btrim(r.revision_json::jsonb ->> 'authorSubject')) NOT BETWEEN 1 AND 256
                ORDER BY r.draft_id, r.revision_number LIMIT 1;
                IF mismatch IS NOT NULL THEN
                    RAISE EXCEPTION 'Cannot set the author: revision % has no valid author', mismatch;
                END IF;
            END
            $check$
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE document_change_drafts, document_change_revisions IN ACCESS EXCLUSIVE MODE");

            migrationBuilder.AddColumn<string>(
                name: "author_kind",
                table: "document_change_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "author_subject",
                table: "document_change_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "change_date",
                table: "document_change_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "change_date_evidence_extraction_id",
                table: "document_change_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "change_date_evidence_length",
                table: "document_change_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "change_date_evidence_start",
                table: "document_change_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "comparison_id",
                table: "document_change_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "document_change_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "institution",
                table: "document_change_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "limits",
                table: "document_change_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "significance",
                table: "document_change_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "document_change_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(CheckSql);
            migrationBuilder.Sql("""
                UPDATE document_change_revisions SET author_subject = revision_json::jsonb ->> 'authorSubject',
                    author_kind = CASE revision_json::jsonb ->> 'authorSubject'
                        WHEN 'civic-lens:analysis' THEN 'Analysis' WHEN 'civic-lens:code' THEN 'Code' ELSE 'Person' END
                """);
            migrationBuilder.Sql("ALTER TABLE document_change_revisions ALTER COLUMN author_kind DROP DEFAULT, ALTER COLUMN author_subject DROP DEFAULT");

            migrationBuilder.AddUniqueConstraint(
                name: "ux_document_change_drafts_draft_comparison",
                table: "document_change_drafts",
                columns: new[] { "draft_id", "comparison_id" });

            migrationBuilder.CreateTable(
                name: "document_change_revision_citations",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_citations", x => new { x.draft_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_document_change_revision_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_document_change_revision_citations_length", "citation_length <= 8192");
                    table.CheckConstraint("ck_document_change_revision_citations_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_citations_document_change_revision~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_revision_citations_document_extractions_cit~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revision_issues",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_issues", x => new { x.draft_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_document_change_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_document_change_revision_issues_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_issues_document_change_revisions_d~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revision_officials",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_officials", x => new { x.draft_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_document_change_revision_officials_official", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_document_change_revision_officials_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_officials_document_change_revision~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revisions_change_date_evidence_extraction_id",
                table: "document_change_revisions",
                column: "change_date_evidence_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revisions_draft_id_comparison_id",
                table: "document_change_revisions",
                columns: new[] { "draft_id", "comparison_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_author_kind",
                table: "document_change_revisions",
                sql: "author_kind IN ('Person', 'Analysis', 'Code')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_change_date",
                table: "document_change_revisions",
                sql: "(change_date IS NULL) = (change_date_evidence_extraction_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_change_date_evidence",
                table: "document_change_revisions",
                sql: "(change_date_evidence_extraction_id IS NULL) = (change_date_evidence_start IS NULL) AND (change_date_evidence_extraction_id IS NULL) = (change_date_evidence_length IS NULL) AND change_date_evidence_start >= 0 AND change_date_evidence_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_columns_written",
                table: "document_change_revisions",
                sql: "num_nulls(comparison_id, headline, summary, institution) IN (0, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_comparison",
                table: "document_change_revisions",
                sql: "comparison_id ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_limits",
                table: "document_change_revisions",
                sql: "length(limits) <= 4000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_significance",
                table: "document_change_revisions",
                sql: "length(significance) <= 4000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_summary",
                table: "document_change_revisions",
                sql: "length(summary) <= 8000");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revision_citations_citation_extraction_id",
                table: "document_change_revision_citations",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revision_citations_draft_id_revision_number~",
                table: "document_change_revision_citations",
                columns: new[] { "draft_id", "revision_number", "citation_extraction_id", "citation_start", "citation_length" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_revisions_document_change_drafts_draft_id_c~",
                table: "document_change_revisions",
                columns: new[] { "draft_id", "comparison_id" },
                principalTable: "document_change_drafts",
                principalColumns: new[] { "draft_id", "comparison_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_revisions_document_extractions_change_date_~",
                table: "document_change_revisions",
                column: "change_date_evidence_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_change_revisions_document_change_drafts_draft_id_c~",
                table: "document_change_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_revisions_document_extractions_change_date_~",
                table: "document_change_revisions");

            migrationBuilder.DropTable(
                name: "document_change_revision_citations");

            migrationBuilder.DropTable(
                name: "document_change_revision_issues");

            migrationBuilder.DropTable(
                name: "document_change_revision_officials");

            migrationBuilder.DropIndex(
                name: "IX_document_change_revisions_change_date_evidence_extraction_id",
                table: "document_change_revisions");

            migrationBuilder.DropIndex(
                name: "IX_document_change_revisions_draft_id_comparison_id",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_author_kind",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_change_date",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_change_date_evidence",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_columns_written",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_comparison",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_limits",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_significance",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_summary",
                table: "document_change_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_document_change_drafts_draft_comparison",
                table: "document_change_drafts");

            migrationBuilder.DropColumn(
                name: "author_kind",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "author_subject",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "change_date",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "change_date_evidence_extraction_id",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "change_date_evidence_length",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "change_date_evidence_start",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "comparison_id",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "institution",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "limits",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "significance",
                table: "document_change_revisions");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "document_change_revisions");
        }
    }
}
