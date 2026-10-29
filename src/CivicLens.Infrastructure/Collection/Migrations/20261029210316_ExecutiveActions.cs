using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "executive_action_drafts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_drafts", x => x.record_id);
                    table.CheckConstraint("ck_executive_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_executive_action_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_executive_action_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_executive_action_drafts_revision", "current_revision_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "executive_action_revisions",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_executive_action_revisions_number", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_executive_action_revisions_executive_action_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "executive_action_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_documents",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    published_on = table.Column<DateOnly>(type: "date", nullable: false),
                    document_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_documents", x => new { x.source_id, x.document_number });
                    table.CheckConstraint("ck_executive_action_documents_revision", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_executive_action_documents_collection_jobs_document_job_id",
                        column: x => x.document_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_documents_executive_action_revisions_recor~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_review_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    decision_json = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_review_decisions", x => x.decision_id);
                    table.CheckConstraint("ck_executive_action_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_executive_action_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_executive_action_review_decisions_executive_action_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_documents_document_job_id",
                table: "executive_action_documents",
                column: "document_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_documents_published",
                table: "executive_action_documents",
                columns: new[] { "source_id", "published_on" });

            migrationBuilder.CreateIndex(
                name: "ux_executive_action_documents_revision",
                table: "executive_action_documents",
                columns: new[] { "record_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_issue_ids",
                table: "executive_action_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_official_ids",
                table: "executive_action_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decisions_record_id_review_state_ve~",
                table: "executive_action_review_decisions",
                columns: new[] { "record_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decisions_record_id_revision_number",
                table: "executive_action_review_decisions",
                columns: new[] { "record_id", "revision_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "executive_action_documents");

            migrationBuilder.DropTable(
                name: "executive_action_review_decisions");

            migrationBuilder.DropTable(
                name: "executive_action_revisions");

            migrationBuilder.DropTable(
                name: "executive_action_drafts");
        }
    }
}
