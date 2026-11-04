using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BriefRevisionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE brief_drafts, brief_revisions IN ACCESS EXCLUSIVE MODE");

            migrationBuilder.AddColumn<int>(
                name: "coverage_considered",
                table: "brief_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "coverage_sent_to_model",
                table: "brief_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cutoff_release_number",
                table: "brief_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                table: "brief_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "first_release_number",
                table: "brief_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "brief_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_fingerprint",
                table: "brief_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_headline",
                table: "brief_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_run_id",
                table: "brief_revisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "text_origin",
                table: "brief_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "week_start",
                table: "brief_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "window_end_utc_ticks",
                table: "brief_revisions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "window_start_utc_ticks",
                table: "brief_revisions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ux_brief_drafts_record_week",
                table: "brief_drafts",
                columns: new[] { "record_id", "week_start" });

            migrationBuilder.CreateTable(
                name: "brief_revision_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    citation_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_revision_number = table.Column<int>(type: "integer", nullable: true),
                    action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    document_change_draft_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    document_change_revision_number = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_citations", x => new { x.record_id, x.revision_number, x.part, x.citation_id });
                    table.CheckConstraint("ck_brief_revision_citations_cited", "(kind = 'Statement') = (statement_candidate_id IS NOT NULL) AND (statement_candidate_id IS NULL) = (statement_revision_number IS NULL) AND (kind = 'Action') = (action_record_id IS NOT NULL) AND (action_record_id IS NULL) = (action_revision_number IS NULL) AND (kind = 'DocumentChange') = (document_change_draft_id IS NOT NULL) AND (document_change_draft_id IS NULL) = (document_change_revision_number IS NULL) AND coalesce(statement_revision_number, action_revision_number, document_change_revision_number) >= 1");
                    table.CheckConstraint("ck_brief_revision_citations_id", "citation_id ~ '^R[1-9][0-9]{0,3}$'");
                    table.CheckConstraint("ck_brief_revision_citations_kind", "kind IN ('DocumentChange', 'Statement', 'Action')");
                    table.CheckConstraint("ck_brief_revision_citations_part", "part IN ('Text', 'Suggestion')");
                    table.CheckConstraint("ck_brief_revision_citations_position", "position BETWEEN 0 AND 255");
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_action_revisions_action_record_id_~",
                        columns: x => new { x.action_record_id, x.action_revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_brief_revisions_record_id_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_document_change_revisions_document~",
                        columns: x => new { x.document_change_draft_id, x.document_change_revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_statement_revisions_statement_cand~",
                        columns: x => new { x.statement_candidate_id, x.statement_revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revision_paragraphs",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    paragraph_text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_paragraphs", x => new { x.record_id, x.revision_number, x.part, x.position });
                    table.CheckConstraint("ck_brief_revision_paragraphs_part", "part IN ('Text', 'Suggestion')");
                    table.CheckConstraint("ck_brief_revision_paragraphs_position", "position BETWEEN 0 AND 11");
                    table.CheckConstraint("ck_brief_revision_paragraphs_text", "length(btrim(paragraph_text)) BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraphs_brief_revisions_record_id_revisio~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revision_paragraph_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paragraph_position = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_paragraph_citations", x => new { x.record_id, x.revision_number, x.part, x.paragraph_position, x.position });
                    table.CheckConstraint("ck_brief_revision_paragraph_citations_position", "position BETWEEN 0 AND 255");
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraph_citations_brief_revision_citations~",
                        columns: x => new { x.record_id, x.revision_number, x.part, x.citation_id },
                        principalTable: "brief_revision_citations",
                        principalColumns: new[] { "record_id", "revision_number", "part", "citation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraph_citations_brief_revision_paragraph~",
                        columns: x => new { x.record_id, x.revision_number, x.part, x.paragraph_position },
                        principalTable: "brief_revision_paragraphs",
                        principalColumns: new[] { "record_id", "revision_number", "part", "position" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_record_id_week_start",
                table: "brief_revisions",
                columns: new[] { "record_id", "week_start" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_suggestion_run_id",
                table: "brief_revisions",
                column: "suggestion_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_columns_written",
                table: "brief_revisions",
                sql: "num_nulls(week_start, window_start_utc_ticks, window_end_utc_ticks, first_release_number, cutoff_release_number, fingerprint, headline, text_origin, coverage_considered, coverage_sent_to_model) IN (0, 10)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_coverage",
                table: "brief_revisions",
                sql: "coverage_sent_to_model >= 0 AND coverage_sent_to_model <= coverage_considered");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_fingerprint",
                table: "brief_revisions",
                sql: "fingerprint ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_headline",
                table: "brief_revisions",
                sql: "length(btrim(headline)) BETWEEN 1 AND 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_person_text",
                table: "brief_revisions",
                sql: "author_kind <> 'Person' OR text_origin = 'Person'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_suggestion",
                table: "brief_revisions",
                sql: "(suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL) AND length(btrim(suggestion_headline)) BETWEEN 1 AND 256 AND suggestion_run_id ~ '^[0-9a-f]{32}$' AND suggestion_fingerprint ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_text_origin",
                table: "brief_revisions",
                sql: "text_origin IN ('Model', 'Person', 'Code')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_window",
                table: "brief_revisions",
                sql: "window_end_utc_ticks - window_start_utc_ticks BETWEEN 6012000000000 AND 6084000000000 AND first_release_number >= 1 AND first_release_number <= cutoff_release_number");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_window_end_utc_ticks",
                table: "brief_revisions",
                sql: "window_end_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_window_start_utc_ticks",
                table: "brief_revisions",
                sql: "window_start_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_action_record_id_action_revision_n~",
                table: "brief_revision_citations",
                columns: new[] { "action_record_id", "action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_document_change_draft_id_document_~",
                table: "brief_revision_citations",
                columns: new[] { "document_change_draft_id", "document_change_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_act~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "action_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_doc~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "document_change_draft_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_sta~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "statement_candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_statement_candidate_id_statement_r~",
                table: "brief_revision_citations",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_paragraph_citations_record_id_revision_numb~1",
                table: "brief_revision_paragraph_citations",
                columns: new[] { "record_id", "revision_number", "part", "paragraph_position", "citation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_paragraph_citations_record_id_revision_numbe~",
                table: "brief_revision_paragraph_citations",
                columns: new[] { "record_id", "revision_number", "part", "citation_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_brief_revisions_brief_analysis_runs_suggestion_run_id",
                table: "brief_revisions",
                column: "suggestion_run_id",
                principalTable: "brief_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_revisions_brief_drafts_record_id_week_start",
                table: "brief_revisions",
                columns: new[] { "record_id", "week_start" },
                principalTable: "brief_drafts",
                principalColumns: new[] { "record_id", "week_start" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_brief_revisions_brief_analysis_runs_suggestion_run_id",
                table: "brief_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_revisions_brief_drafts_record_id_week_start",
                table: "brief_revisions");

            migrationBuilder.DropTable(
                name: "brief_revision_paragraph_citations");

            migrationBuilder.DropTable(
                name: "brief_revision_citations");

            migrationBuilder.DropTable(
                name: "brief_revision_paragraphs");

            migrationBuilder.DropIndex(
                name: "IX_brief_revisions_record_id_week_start",
                table: "brief_revisions");

            migrationBuilder.DropIndex(
                name: "IX_brief_revisions_suggestion_run_id",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_columns_written",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_coverage",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_fingerprint",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_headline",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_person_text",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_suggestion",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_text_origin",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_window",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_window_end_utc_ticks",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_window_start_utc_ticks",
                table: "brief_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_brief_drafts_record_week",
                table: "brief_drafts");

            migrationBuilder.DropColumn(
                name: "coverage_considered",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "coverage_sent_to_model",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "cutoff_release_number",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "fingerprint",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "first_release_number",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_fingerprint",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_headline",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_run_id",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "text_origin",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "week_start",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "window_end_utc_ticks",
                table: "brief_revisions");

            migrationBuilder.DropColumn(
                name: "window_start_utc_ticks",
                table: "brief_revisions");
        }
    }
}
