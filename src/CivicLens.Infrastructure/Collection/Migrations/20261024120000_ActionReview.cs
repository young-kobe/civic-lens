using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261024120000_ActionReview")]
public partial class ActionReview : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE action_drafts ADD COLUMN review_state_version integer NOT NULL DEFAULT 0;
            ALTER TABLE action_drafts ALTER COLUMN review_state_version DROP DEFAULT;
            ALTER TABLE action_drafts ADD COLUMN review_status character varying(32) NOT NULL DEFAULT 'AwaitingReview';
            ALTER TABLE action_drafts ALTER COLUMN review_status DROP DEFAULT;
            ALTER TABLE action_drafts ADD COLUMN last_revised_at_utc_ticks bigint;
            UPDATE action_drafts d
               SET last_revised_at_utc_ticks = r.created_at_utc_ticks
              FROM action_revisions r
             WHERE r.record_id = d.record_id AND r.revision_number = d.current_revision_number;
            ALTER TABLE action_drafts ALTER COLUMN last_revised_at_utc_ticks SET NOT NULL;
            ALTER TABLE action_drafts ADD CONSTRAINT ck_action_drafts_review_state_version CHECK (review_state_version >= 0);
            ALTER TABLE action_drafts ADD CONSTRAINT ck_action_drafts_review_status
                CHECK (review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn'));
            ALTER TABLE action_analysis_runs DROP CONSTRAINT ck_action_analysis_runs_reason;
            ALTER TABLE action_analysis_runs ADD CONSTRAINT ck_action_analysis_runs_reason
                CHECK (reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged'));
            """);

        migrationBuilder.CreateTable(
            name: "action_review_decisions",
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
                table.PrimaryKey("PK_action_review_decisions", x => x.decision_id);
                table.CheckConstraint("ck_action_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_action_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                table.ForeignKey(
                    name: "FK_action_review_decisions_action_revisions_record_id_revision_number",
                    columns: x => new { x.record_id, x.revision_number },
                    principalTable: "action_revisions",
                    principalColumns: new[] { "record_id", "revision_number" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_action_review_decisions_record_id_review_state_version",
            table: "action_review_decisions",
            columns: new[] { "record_id", "review_state_version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_action_review_decisions_record_id_revision_number",
            table: "action_review_decisions",
            columns: new[] { "record_id", "revision_number" });

        migrationBuilder.Sql("""
            CREATE INDEX ix_action_drafts_approved_created
                ON action_drafts (created_at_utc_ticks DESC, record_id DESC) WHERE review_status = 'Approved';
            CREATE INDEX ix_action_drafts_needs_action_revised
                ON action_drafts (last_revised_at_utc_ticks DESC, record_id DESC) WHERE review_status <> 'Approved';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_action_drafts_needs_action_revised;
            DROP INDEX IF EXISTS ix_action_drafts_approved_created;
            """);
        migrationBuilder.DropTable(name: "action_review_decisions");
        migrationBuilder.Sql("""
            ALTER TABLE action_analysis_runs DROP CONSTRAINT ck_action_analysis_runs_reason;
            ALTER TABLE action_analysis_runs ADD CONSTRAINT ck_action_analysis_runs_reason
                CHECK (reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted'));
            ALTER TABLE action_drafts DROP CONSTRAINT ck_action_drafts_review_status;
            ALTER TABLE action_drafts DROP CONSTRAINT ck_action_drafts_review_state_version;
            ALTER TABLE action_drafts DROP COLUMN last_revised_at_utc_ticks;
            ALTER TABLE action_drafts DROP COLUMN review_status;
            ALTER TABLE action_drafts DROP COLUMN review_state_version;
            """);
    }
}
