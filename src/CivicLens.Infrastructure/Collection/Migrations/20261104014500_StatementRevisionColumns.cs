using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class StatementRevisionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("LOCK TABLE statement_candidates, statement_drafts, statement_revisions IN ACCESS EXCLUSIVE MODE");

            migrationBuilder.AddColumn<string>(
                name: "headline",
                table: "statement_revisions",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "statement_date",
                table: "statement_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "statement_date_evidence_extraction_id",
                table: "statement_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "statement_date_evidence_length",
                table: "statement_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "statement_date_evidence_start",
                table: "statement_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary",
                table: "statement_revisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ux_statement_candidates_candidate_extraction",
                table: "statement_candidates",
                columns: new[] { "candidate_id", "extraction_id" });

            migrationBuilder.CreateTable(
                name: "statement_revision_bill_numbers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    bill_number = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_bill_numbers", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_bill_numbers_bill_number", "length(btrim(bill_number)) > 0");
                    table.CheckConstraint("ck_statement_revision_bill_numbers_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_statement_revision_bill_numbers_statement_revisions_candida~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_issues",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_issues", x => new { x.candidate_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_statement_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_issues_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_issues_statement_revisions_candidate_id_~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_other_speakers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    speaker_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_other_speakers", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_other_speakers_name", "length(speaker_name) BETWEEN 1 AND 128 AND speaker_name = btrim(speaker_name)");
                    table.CheckConstraint("ck_statement_revision_other_speakers_position", "position BETWEEN 0 AND 15");
                    table.ForeignKey(
                        name: "FK_statement_revision_other_speakers_statement_revisions_candi~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_policy_areas",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_policy_areas", x => new { x.candidate_id, x.revision_number, x.policy_area_id });
                    table.CheckConstraint("ck_statement_revision_policy_areas_policy_area", "length(btrim(policy_area_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_policy_areas_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_policy_areas_statement_revisions_candida~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_quotations",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    quotation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quotation_start = table.Column<int>(type: "integer", nullable: false),
                    quotation_length = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_quotations", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_quotations_kind", "kind IN ('DirectQuotation', 'ReleaseText')");
                    table.CheckConstraint("ck_statement_revision_quotations_length", "quotation_length <= 4096");
                    table.CheckConstraint("ck_statement_revision_quotations_position", "position BETWEEN 0 AND 31");
                    table.CheckConstraint("ck_statement_revision_quotations_quotation", "quotation_start >= 0 AND quotation_length > 0");
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_document_extractions_quotatio~",
                        column: x => x.quotation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_statement_candidates_candidat~",
                        columns: x => new { x.candidate_id, x.quotation_extraction_id },
                        principalTable: "statement_candidates",
                        principalColumns: new[] { "candidate_id", "extraction_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_statement_revisions_candidate~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_speakers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_speakers", x => new { x.candidate_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_statement_revision_speakers_official", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_speakers_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_speakers_statement_revisions_candidate_i~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_candidate_id_statement_date_evidence_ex~",
                table: "statement_revisions",
                columns: new[] { "candidate_id", "statement_date_evidence_extraction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_statement_date_evidence_extraction_id",
                table: "statement_revisions",
                column: "statement_date_evidence_extraction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_columns_written",
                table: "statement_revisions",
                sql: "num_nulls(headline, summary) IN (0, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_date_evidence",
                table: "statement_revisions",
                sql: "(statement_date IS NULL) = (statement_date_evidence_extraction_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_date_evidence_length",
                table: "statement_revisions",
                sql: "statement_date_evidence_length <= 4096");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_statement_date_evidence",
                table: "statement_revisions",
                sql: "(statement_date_evidence_extraction_id IS NULL) = (statement_date_evidence_start IS NULL) AND (statement_date_evidence_extraction_id IS NULL) = (statement_date_evidence_length IS NULL) AND statement_date_evidence_start >= 0 AND statement_date_evidence_length > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_summary",
                table: "statement_revisions",
                sql: "length(summary) <= 8000");

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_bill_numbers_bill_number",
                table: "statement_revision_bill_numbers",
                column: "bill_number");

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_other_speakers_candidate_id_revision_num~",
                table: "statement_revision_other_speakers",
                columns: new[] { "candidate_id", "revision_number", "speaker_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_candidate_id_quotation_extrac~",
                table: "statement_revision_quotations",
                columns: new[] { "candidate_id", "quotation_extraction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_candidate_id_revision_number_~",
                table: "statement_revision_quotations",
                columns: new[] { "candidate_id", "revision_number", "quotation_extraction_id", "quotation_start", "quotation_length" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_quotation_extraction_id",
                table: "statement_revision_quotations",
                column: "quotation_extraction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_statement_revisions_document_extractions_statement_date_evi~",
                table: "statement_revisions",
                column: "statement_date_evidence_extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_revisions_statement_candidates_candidate_id_state~",
                table: "statement_revisions",
                columns: new[] { "candidate_id", "statement_date_evidence_extraction_id" },
                principalTable: "statement_candidates",
                principalColumns: new[] { "candidate_id", "extraction_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_statement_revisions_document_extractions_statement_date_evi~",
                table: "statement_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_revisions_statement_candidates_candidate_id_state~",
                table: "statement_revisions");

            migrationBuilder.DropTable(
                name: "statement_revision_bill_numbers");

            migrationBuilder.DropTable(
                name: "statement_revision_issues");

            migrationBuilder.DropTable(
                name: "statement_revision_other_speakers");

            migrationBuilder.DropTable(
                name: "statement_revision_policy_areas");

            migrationBuilder.DropTable(
                name: "statement_revision_quotations");

            migrationBuilder.DropTable(
                name: "statement_revision_speakers");

            migrationBuilder.DropIndex(
                name: "IX_statement_revisions_candidate_id_statement_date_evidence_ex~",
                table: "statement_revisions");

            migrationBuilder.DropIndex(
                name: "IX_statement_revisions_statement_date_evidence_extraction_id",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_columns_written",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_date_evidence",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_date_evidence_length",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_statement_date_evidence",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_summary",
                table: "statement_revisions");

            migrationBuilder.DropUniqueConstraint(
                name: "ux_statement_candidates_candidate_extraction",
                table: "statement_candidates");

            migrationBuilder.DropColumn(
                name: "headline",
                table: "statement_revisions");

            migrationBuilder.DropColumn(
                name: "statement_date",
                table: "statement_revisions");

            migrationBuilder.DropColumn(
                name: "statement_date_evidence_extraction_id",
                table: "statement_revisions");

            migrationBuilder.DropColumn(
                name: "statement_date_evidence_length",
                table: "statement_revisions");

            migrationBuilder.DropColumn(
                name: "statement_date_evidence_start",
                table: "statement_revisions");

            migrationBuilder.DropColumn(
                name: "summary",
                table: "statement_revisions");
        }
    }
}
