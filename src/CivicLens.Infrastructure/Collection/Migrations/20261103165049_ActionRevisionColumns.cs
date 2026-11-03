using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bill_citation_extraction_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bill_citation_length",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bill_citation_start",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bill_number",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bill_title",
                table: "action_revisions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bill_type",
                table: "action_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "candidate_includes_subjects",
                table: "action_revisions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "candidate_includes_summaries",
                table: "action_revisions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "candidate_update_utc_ticks",
                table: "action_revisions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "congress",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fingerprint",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "action_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "issue_candidates_recorded",
                table: "action_revisions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issue_revision_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_area",
                table: "action_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_area_citation_extraction_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "policy_area_citation_length",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "policy_area_citation_start",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "policy_area_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_id",
                table: "action_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subjects_hash",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_fingerprint",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_headline",
                table: "action_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_run_id",
                table: "action_revisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_summary",
                table: "action_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "action_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_citation_extraction_id",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "summary_citation_length",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "summary_citation_start",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_version",
                table: "action_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "text_origin",
                table: "action_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ux_action_drafts_record_bill",
                table: "action_drafts",
                columns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" });

            migrationBuilder.CreateTable(
                name: "action_revision_issues",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    is_tracked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_issues", x => new { x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_action_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_action_revision_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_action_revision_issues_action_revisions_record_id_revision_~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_sponsorships",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    person_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_original = table.Column<bool>(type: "boolean", nullable: false),
                    sponsored_on = table.Column<DateOnly>(type: "date", nullable: false),
                    withdrawn_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_sponsorships", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_sponsorships_person", "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0");
                    table.CheckConstraint("ck_action_revision_sponsorships_position", "position >= 0");
                    table.CheckConstraint("ck_action_revision_sponsorships_role", "role IN ('Sponsor', 'Cosponsor')");
                    table.CheckConstraint("ck_action_revision_sponsorships_withdrawn", "withdrawn_on >= sponsored_on");
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorships_action_revisions_record_id_rev~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_sponsorship_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    sponsorship_position = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_sponsorship_citations", x => new { x.record_id, x.revision_number, x.sponsorship_position, x.position });
                    table.CheckConstraint("ck_action_revision_sponsorship_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_sponsorship_citations_position", "position BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorship_citations_action_revision_spons~",
                        columns: x => new { x.record_id, x.revision_number, x.sponsorship_position },
                        principalTable: "action_revision_sponsorships",
                        principalColumns: new[] { "record_id", "revision_number", "position" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorship_citations_document_extractions_~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_bill_citation_extraction_id",
                table: "action_revisions",
                column: "bill_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_candidate_id",
                table: "action_revisions",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_policy_area_citation_extraction_id",
                table: "action_revisions",
                column: "policy_area_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_record_id_source_id_congress_bill_type_bil~",
                table: "action_revisions",
                columns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" });

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_suggestion_run_id",
                table: "action_revisions",
                column: "suggestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_summary_citation_extraction_id",
                table: "action_revisions",
                column: "summary_citation_extraction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_bill",
                table: "action_revisions",
                sql: "congress >= 1 AND bill_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_bill_citation",
                table: "action_revisions",
                sql: "(bill_citation_extraction_id IS NULL) = (bill_citation_start IS NULL) AND (bill_citation_extraction_id IS NULL) = (bill_citation_length IS NULL) AND bill_citation_start >= 0 AND bill_citation_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_candidate",
                table: "action_revisions",
                sql: "candidate_id ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_candidate_update_utc_ticks",
                table: "action_revisions",
                sql: "candidate_update_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_code_bill_citation",
                table: "action_revisions",
                sql: "author_kind <> 'Code' OR headline IS NULL OR bill_citation_extraction_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions",
                sql: "num_nulls(headline, summary, text_origin, source_id, congress, bill_type, bill_number, candidate_id, candidate_update_utc_ticks, candidate_includes_summaries, candidate_includes_subjects, fingerprint, bill_title, issue_revision_id, issue_candidates_recorded) IN (0, 15)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_fingerprint",
                table: "action_revisions",
                sql: "fingerprint ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_headline",
                table: "action_revisions",
                sql: "length(btrim(headline)) BETWEEN 1 AND 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_issue_revision",
                table: "action_revisions",
                sql: "issue_revision_id ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_person_text",
                table: "action_revisions",
                sql: "author_kind <> 'Person' OR text_origin = 'Person'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_policy_area",
                table: "action_revisions",
                sql: "(policy_area IS NULL) = (policy_area_citation_extraction_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_policy_area_citation",
                table: "action_revisions",
                sql: "(policy_area_citation_extraction_id IS NULL) = (policy_area_citation_start IS NULL) AND (policy_area_citation_extraction_id IS NULL) = (policy_area_citation_length IS NULL) AND policy_area_citation_start >= 0 AND policy_area_citation_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_policy_area_id",
                table: "action_revisions",
                sql: "policy_area_id IS NULL OR policy_area IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_subjects_hash",
                table: "action_revisions",
                sql: "subjects_hash ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_suggestion",
                table: "action_revisions",
                sql: "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_summary",
                table: "action_revisions",
                sql: "length(summary) BETWEEN 1 AND 8000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_summary_citation",
                table: "action_revisions",
                sql: "(summary_citation_extraction_id IS NULL) = (summary_citation_start IS NULL) AND (summary_citation_extraction_id IS NULL) = (summary_citation_length IS NULL) AND summary_citation_start >= 0 AND summary_citation_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_summary_version",
                table: "action_revisions",
                sql: "(summary_version IS NULL) = (summary_citation_extraction_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_text_origin",
                table: "action_revisions",
                sql: "text_origin IN ('Model', 'Person', 'Code')");

            migrationBuilder.CreateIndex(
                name: "ix_action_revision_issues_issue",
                table: "action_revision_issues",
                columns: new[] { "issue_id", "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_action_revision_sponsorship_citations_citation_extraction_id",
                table: "action_revision_sponsorship_citations",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_revision_sponsorships_person",
                table: "action_revision_sponsorships",
                columns: new[] { "person_id", "record_id", "revision_number" });

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_action_analysis_runs_suggestion_run_id",
                table: "action_revisions",
                column: "suggestion_run_id",
                principalTable: "action_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_action_candidates_candidate_id",
                table: "action_revisions",
                column: "candidate_id",
                principalTable: "action_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_action_drafts_record_id_source_id_congress~",
                table: "action_revisions",
                columns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" },
                principalTable: "action_drafts",
                principalColumns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_document_extractions_bill_citation_extract~",
                table: "action_revisions",
                column: "bill_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_document_extractions_policy_area_citation_~",
                table: "action_revisions",
                column: "policy_area_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_revisions_document_extractions_summary_citation_extr~",
                table: "action_revisions",
                column: "summary_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_action_analysis_runs_suggestion_run_id",
                table: "action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_action_candidates_candidate_id",
                table: "action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_action_drafts_record_id_source_id_congress~",
                table: "action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_document_extractions_bill_citation_extract~",
                table: "action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_document_extractions_policy_area_citation_~",
                table: "action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_action_revisions_document_extractions_summary_citation_extr~",
                table: "action_revisions");

            migrationBuilder.DropTable(
                name: "action_revision_issues");

            migrationBuilder.DropTable(
                name: "action_revision_sponsorship_citations");

            migrationBuilder.DropTable(
                name: "action_revision_sponsorships");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_bill_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_candidate_id",
                table: "action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_policy_area_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_record_id_source_id_congress_bill_type_bil~",
                table: "action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_suggestion_run_id",
                table: "action_revisions");

            migrationBuilder.DropIndex(
                name: "IX_action_revisions_summary_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_bill",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_bill_citation",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_candidate",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_candidate_update_utc_ticks",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_code_bill_citation",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_fingerprint",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_headline",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_issue_revision",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_person_text",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_policy_area",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_policy_area_citation",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_policy_area_id",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_subjects_hash",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_suggestion",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_summary",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_summary_citation",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_summary_version",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_text_origin",
                table: "action_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_action_drafts_record_bill",
                table: "action_drafts");

            migrationBuilder.DropColumn(
                name: "bill_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "bill_citation_length",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "bill_citation_start",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "bill_number",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "bill_title",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "bill_type",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_includes_subjects",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_includes_summaries",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_update_utc_ticks",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "congress",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "fingerprint",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "issue_candidates_recorded",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "issue_revision_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "policy_area",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "policy_area_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "policy_area_citation_length",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "policy_area_citation_start",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "policy_area_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "subjects_hash",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_fingerprint",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_headline",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_run_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_summary",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_citation_extraction_id",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_citation_length",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_citation_start",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_version",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "text_origin",
                table: "action_revisions");
        }
    }
}
