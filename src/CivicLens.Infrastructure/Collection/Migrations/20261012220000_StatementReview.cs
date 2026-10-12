using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012220000_StatementReview")]
public partial class StatementReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE statement_drafts ADD COLUMN review_state_version integer NOT NULL DEFAULT 0;
            ALTER TABLE statement_drafts ALTER COLUMN review_state_version DROP DEFAULT;
            ALTER TABLE statement_drafts ADD COLUMN review_status character varying(32);
            UPDATE statement_drafts
               SET review_status = CASE WHEN current_revision_number = 0 THEN 'NotDrafted' ELSE 'AwaitingReview' END;
            ALTER TABLE statement_drafts ALTER COLUMN review_status SET NOT NULL;
            ALTER TABLE statement_drafts ADD CONSTRAINT ck_statement_drafts_review_state_version CHECK (review_state_version >= 0);
            ALTER TABLE statement_drafts ADD CONSTRAINT ck_statement_drafts_review_status
                CHECK (review_status IN ('NotDrafted','AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn'));
            ALTER TABLE statement_drafts ADD CONSTRAINT ck_statement_drafts_not_drafted
                CHECK ((current_revision_number = 0) = (review_status = 'NotDrafted'));
            """);

        migrationBuilder.CreateTable(
            name: "statement_review_decisions",
            columns: table => new
            {
                decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                review_state_version = table.Column<int>(type: "integer", nullable: false),
                decision_json = table.Column<string>(type: "text", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_review_decisions", x => x.decision_id);
                table.CheckConstraint("ck_statement_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_statement_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                table.ForeignKey(
                    name: "FK_statement_review_decisions_statement_revisions_candidate_id_revision_number",
                    columns: x => new { x.candidate_id, x.revision_number },
                    principalTable: "statement_revisions",
                    principalColumns: new[] { "candidate_id", "revision_number" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_statement_review_decisions_candidate_id_review_state_version",
            table: "statement_review_decisions",
            columns: new[] { "candidate_id", "review_state_version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_statement_review_decisions_candidate_id_revision_number",
            table: "statement_review_decisions",
            columns: new[] { "candidate_id", "revision_number" });

        migrationBuilder.Sql("""
            CREATE INDEX ix_statement_drafts_approved_created
                ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status = 'Approved';
            CREATE INDEX ix_statement_drafts_needs_action_created
                ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status <> 'Approved';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_statement_drafts_needs_action_created;
            DROP INDEX IF EXISTS ix_statement_drafts_approved_created;
            """);
        migrationBuilder.DropTable(name: "statement_review_decisions");
        migrationBuilder.Sql("""
            ALTER TABLE statement_drafts DROP CONSTRAINT ck_statement_drafts_not_drafted;
            ALTER TABLE statement_drafts DROP CONSTRAINT ck_statement_drafts_review_status;
            ALTER TABLE statement_drafts DROP CONSTRAINT ck_statement_drafts_review_state_version;
            ALTER TABLE statement_drafts DROP COLUMN review_status;
            ALTER TABLE statement_drafts DROP COLUMN review_state_version;
            """);
    }
}
