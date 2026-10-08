using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class DocumentChangeReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_change_drafts",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_drafts", x => x.draft_id);
                    table.CheckConstraint("ck_document_change_drafts_id", "draft_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_drafts_versions", "current_revision_number >= 1 AND review_state_version >= 0");
                    table.ForeignKey(
                        name: "FK_document_change_drafts_document_comparisons_comparison_id",
                        column: x => x.comparison_id,
                        principalTable: "document_comparisons",
                        principalColumn: "comparison_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_review_idempotency",
                columns: table => new
                {
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_review_idempotency", x => new { x.actor_subject, x.operation, x.idempotency_key });
                    table.CheckConstraint("ck_document_change_review_idempotency_hash", "payload_hash ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "document_change_revisions",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    revision_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revisions", x => new { x.draft_id, x.revision_number });
                    table.CheckConstraint("ck_document_change_revisions_number", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_document_change_revisions_document_change_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "document_change_drafts",
                        principalColumn: "draft_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_review_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    decision_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_review_decisions", x => x.decision_id);
                    table.CheckConstraint("ck_document_change_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_document_change_review_decisions_document_change_revisions_~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_change_drafts_comparison_id",
                table: "document_change_drafts",
                column: "comparison_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decisions_draft_id_review_state_vers~",
                table: "document_change_review_decisions",
                columns: new[] { "draft_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decisions_draft_id_revision_number",
                table: "document_change_review_decisions",
                columns: new[] { "draft_id", "revision_number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_change_review_decisions");

            migrationBuilder.DropTable(
                name: "document_change_review_idempotency");

            migrationBuilder.DropTable(
                name: "document_change_revisions");

            migrationBuilder.DropTable(
                name: "document_change_drafts");
        }
    }
}
