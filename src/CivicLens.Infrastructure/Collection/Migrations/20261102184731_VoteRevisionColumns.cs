using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class VoteRevisionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "bill_number",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bill_type",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "candidate_id",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "candidate_update_utc_ticks",
                table: "vote_revisions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "chamber",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "congress",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "vote_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "issue_candidates_recorded",
                table: "vote_revisions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "issue_revision_id",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "vote_revisions",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind_citation_extraction_id",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind_citation_length",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "kind_citation_start",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "member_list_id",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "roll_call_number",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "session_number",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_id",
                table: "vote_revisions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subjects_hash",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_fingerprint",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_headline",
                table: "vote_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_run_id",
                table: "vote_revisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suggestion_summary",
                table: "vote_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "vote_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "text_origin",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vote_source",
                table: "vote_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "voted_on",
                table: "vote_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voted_on_citation_extraction_id",
                table: "vote_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voted_on_citation_length",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voted_on_citation_start",
                table: "vote_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ux_vote_drafts_record_roll",
                table: "vote_drafts",
                columns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" });

            migrationBuilder.CreateTable(
                name: "vote_member_lists",
                columns: table => new
                {
                    list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    member_count = table.Column<short>(type: "smallint", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_member_lists", x => x.list_id);
                    table.CheckConstraint("ck_vote_member_lists_count", "member_count BETWEEN 1 AND 600");
                    table.CheckConstraint("ck_vote_member_lists_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_member_lists_id", "list_id ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "vote_revision_facts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    fact_text = table.Column<string>(type: "text", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_revision_facts", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_vote_revision_facts_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_vote_revision_facts_position", "position >= 0");
                    table.CheckConstraint("ck_vote_revision_facts_text", "length(fact_text) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_vote_revision_facts_document_extractions_citation_extractio~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revision_facts_vote_revisions_record_id_revision_number",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_revision_issues",
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
                    table.PrimaryKey("PK_vote_revision_issues", x => new { x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_vote_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_vote_revision_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_vote_revision_issues_vote_revisions_record_id_revision_numb~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_member_list_entries",
                columns: table => new
                {
                    list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    person_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vote_cast = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_member_list_entries", x => new { x.list_id, x.position });
                    table.CheckConstraint("ck_vote_member_list_entries_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_vote_member_list_entries_member", "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0 AND length(btrim(vote_cast)) > 0");
                    table.CheckConstraint("ck_vote_member_list_entries_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_vote_member_list_entries_document_extractions_citation_extr~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_member_list_entries_vote_member_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "vote_member_lists",
                        principalColumn: "list_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_candidate_id",
                table: "vote_revisions",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_kind_citation_extraction_id",
                table: "vote_revisions",
                column: "kind_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_member_list_id",
                table: "vote_revisions",
                column: "member_list_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_record_id_chamber_congress_session_number_ro~",
                table: "vote_revisions",
                columns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_suggestion_run_id",
                table: "vote_revisions",
                column: "suggestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_voted_on_citation_extraction_id",
                table: "vote_revisions",
                column: "voted_on_citation_extraction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_bill",
                table: "vote_revisions",
                sql: "(bill_type IS NULL) = (bill_number IS NULL) AND bill_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_candidate_update_utc_ticks",
                table: "vote_revisions",
                sql: "candidate_update_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_chamber",
                table: "vote_revisions",
                sql: "chamber IN ('House', 'Senate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_chamber_source",
                table: "vote_revisions",
                sql: "(chamber = 'House') = (vote_source = 'CongressGov')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_columns_written",
                table: "vote_revisions",
                sql: "num_nulls(headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id, candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind, member_list_id, issue_revision_id, issue_candidates_recorded) IN (0, 19)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_headline",
                table: "vote_revisions",
                sql: "length(btrim(headline)) BETWEEN 1 AND 256");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_issue_revision",
                table: "vote_revisions",
                sql: "issue_revision_id ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_kind",
                table: "vote_revisions",
                sql: "kind IN ('Rule', 'PreviousQuestion', 'MotionToRecommit', 'Amendment', 'Suspension', 'Passage', 'ConferenceReport', 'VetoOverride', 'Nomination', 'Cloture', 'MotionToProceed', 'Other')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_kind_citation",
                table: "vote_revisions",
                sql: "(kind_citation_extraction_id IS NULL) = (kind_citation_start IS NULL) AND (kind_citation_extraction_id IS NULL) = (kind_citation_length IS NULL) AND kind_citation_start >= 0 AND kind_citation_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_roll",
                table: "vote_revisions",
                sql: "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_subjects_hash",
                table: "vote_revisions",
                sql: "subjects_hash ~ '^[0-9a-f]{64}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_suggestion",
                table: "vote_revisions",
                sql: "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_summary",
                table: "vote_revisions",
                sql: "length(summary) BETWEEN 1 AND 4000");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_text_origin",
                table: "vote_revisions",
                sql: "text_origin IN ('Model', 'Person', 'Code')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_vote_source",
                table: "vote_revisions",
                sql: "vote_source IN ('CongressGov', 'Voteview')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_voted_on_citation",
                table: "vote_revisions",
                sql: "voted_on_citation_start >= 0 AND voted_on_citation_length > 0");

            migrationBuilder.CreateIndex(
                name: "IX_vote_member_list_entries_citation_extraction_id",
                table: "vote_member_list_entries",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_member_list_entries_person",
                table: "vote_member_list_entries",
                columns: new[] { "person_id", "list_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_revision_facts_citation_extraction_id",
                table: "vote_revision_facts",
                column: "citation_extraction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_document_extractions_kind_citation_extractio~",
                table: "vote_revisions",
                column: "kind_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_document_extractions_voted_on_citation_extra~",
                table: "vote_revisions",
                column: "voted_on_citation_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_vote_analysis_runs_suggestion_run_id",
                table: "vote_revisions",
                column: "suggestion_run_id",
                principalTable: "vote_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_vote_candidates_candidate_id",
                table: "vote_revisions",
                column: "candidate_id",
                principalTable: "vote_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_vote_drafts_record_id_chamber_congress_sessi~",
                table: "vote_revisions",
                columns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" },
                principalTable: "vote_drafts",
                principalColumns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_revisions_vote_member_lists_member_list_id",
                table: "vote_revisions",
                column: "member_list_id",
                principalTable: "vote_member_lists",
                principalColumn: "list_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_document_extractions_kind_citation_extractio~",
                table: "vote_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_document_extractions_voted_on_citation_extra~",
                table: "vote_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_vote_analysis_runs_suggestion_run_id",
                table: "vote_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_vote_candidates_candidate_id",
                table: "vote_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_vote_drafts_record_id_chamber_congress_sessi~",
                table: "vote_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_revisions_vote_member_lists_member_list_id",
                table: "vote_revisions");

            migrationBuilder.DropTable(
                name: "vote_member_list_entries");

            migrationBuilder.DropTable(
                name: "vote_revision_facts");

            migrationBuilder.DropTable(
                name: "vote_revision_issues");

            migrationBuilder.DropTable(
                name: "vote_member_lists");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_candidate_id",
                table: "vote_revisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_kind_citation_extraction_id",
                table: "vote_revisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_member_list_id",
                table: "vote_revisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_record_id_chamber_congress_session_number_ro~",
                table: "vote_revisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_suggestion_run_id",
                table: "vote_revisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_revisions_voted_on_citation_extraction_id",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_bill",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_candidate_update_utc_ticks",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_chamber",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_chamber_source",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_columns_written",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_headline",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_issue_revision",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_kind",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_kind_citation",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_roll",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_subjects_hash",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_suggestion",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_summary",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_text_origin",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_vote_source",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_voted_on_citation",
                table: "vote_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_vote_drafts_record_roll",
                table: "vote_drafts");

            migrationBuilder.DropColumn(
                name: "bill_number",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "bill_type",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "candidate_update_utc_ticks",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "chamber",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "congress",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "issue_candidates_recorded",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "issue_revision_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "kind_citation_extraction_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "kind_citation_length",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "kind_citation_start",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "member_list_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "roll_call_number",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "session_number",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "subjects_hash",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_fingerprint",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_headline",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_run_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "suggestion_summary",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "text_origin",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "vote_source",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "voted_on",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "voted_on_citation_extraction_id",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "voted_on_citation_length",
                table: "vote_revisions");

            migrationBuilder.DropColumn(
                name: "voted_on_citation_start",
                table: "vote_revisions");
        }
    }
}
