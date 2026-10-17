using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261102120000_RelationshipDrafting")]
public partial class RelationshipDrafting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "relationship_analysis",
            columns: table => new
            {
                pair_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                target_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                lease_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                lease_expires_at = table.Column<long>(type: "bigint", nullable: true),
                fence = table.Column<long>(type: "bigint", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                retry_at = table.Column<long>(type: "bigint", nullable: true),
                reserved_tokens = table.Column<long>(type: "bigint", nullable: false),
                budget_day = table.Column<DateOnly>(type: "date", nullable: true),
                reserved_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                pending_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                pending_input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                retry_of_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_relationship_analysis", x => new { x.pair_id, x.task });
                table.CheckConstraint("ck_relationship_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                table.CheckConstraint("ck_relationship_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                table.CheckConstraint("ck_relationship_analysis_pair", "pair_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_relationship_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                table.CheckConstraint("ck_relationship_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_relationship_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                table.CheckConstraint("ck_relationship_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                table.CheckConstraint("ck_relationship_analysis_target_kind", "target_kind IN ('Action','Vote')");
                table.ForeignKey(
                    name: "FK_relationship_analysis_statement_candidates_statement_candid~",
                    column: x => x.statement_candidate_id,
                    principalTable: "statement_candidates",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "relationship_analysis_budget",
            columns: table => new
            {
                day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                charged_tokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_relationship_analysis_budget", x => new { x.day_utc, x.task });
                table.CheckConstraint("ck_relationship_analysis_budget_charge", "charged_tokens >= 0");
            });

        migrationBuilder.CreateTable(
            name: "relationship_drafts",
            columns: table => new
            {
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                current_revision_number = table.Column<int>(type: "integer", nullable: false),
                review_state_version = table.Column<int>(type: "integer", nullable: false),
                review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                no_longer_matches = table.Column<bool>(type: "boolean", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_relationship_drafts", x => x.record_id);
                table.CheckConstraint("ck_relationship_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_relationship_drafts_review_state_version", "review_state_version >= 0");
                table.CheckConstraint("ck_relationship_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                table.CheckConstraint("ck_relationship_drafts_revision", "current_revision_number >= 1");
            });

        migrationBuilder.CreateTable(
            name: "relationship_analysis_runs",
            columns: table => new
            {
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                pair_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                task_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                schema_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                previous_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                input_tokens = table.Column<long>(type: "bigint", nullable: false),
                output_tokens = table.Column<long>(type: "bigint", nullable: false),
                cache_read_tokens = table.Column<long>(type: "bigint", nullable: false),
                cache_write_tokens = table.Column<long>(type: "bigint", nullable: false),
                charged_tokens = table.Column<long>(type: "bigint", nullable: false),
                stop_reason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                provider_request_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                output_json = table.Column<string>(type: "text", nullable: true),
                validation_json = table.Column<string>(type: "text", nullable: true),
                context_json = table.Column<string>(type: "text", nullable: true),
                started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_relationship_analysis_runs", x => x.run_id);
                table.CheckConstraint("ck_relationship_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.CheckConstraint("ck_relationship_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                table.CheckConstraint("ck_relationship_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noRelationship','noLongerMatches')");
                table.CheckConstraint("ck_relationship_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                table.CheckConstraint("ck_relationship_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                table.ForeignKey(
                    name: "FK_relationship_analysis_runs_relationship_analysis_pair_id_ta~",
                    columns: x => new { x.pair_id, x.task },
                    principalTable: "relationship_analysis",
                    principalColumns: new[] { "pair_id", "task" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_relationship_analysis_runs_relationship_analysis_runs_previ~",
                    column: x => x.previous_run_id,
                    principalTable: "relationship_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "relationship_revisions",
            columns: table => new
            {
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                revision_number = table.Column<int>(type: "integer", nullable: false),
                author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                revision_json = table.Column<string>(type: "text", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_relationship_revisions", x => new { x.record_id, x.revision_number });
                table.CheckConstraint("ck_relationship_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                table.CheckConstraint("ck_relationship_revisions_number", "revision_number >= 1");
                table.CheckConstraint("ck_relationship_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_relationship_revisions_relationship_analysis_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "relationship_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_relationship_revisions_relationship_drafts_record_id",
                    column: x => x.record_id,
                    principalTable: "relationship_drafts",
                    principalColumn: "record_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "relationship_review_decisions",
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
                table.PrimaryKey("PK_relationship_review_decisions", x => x.decision_id);
                table.CheckConstraint("ck_relationship_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_relationship_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                table.ForeignKey(
                    name: "FK_relationship_review_decisions_relationship_revisions_record~",
                    columns: x => new { x.record_id, x.revision_number },
                    principalTable: "relationship_revisions",
                    principalColumns: new[] { "record_id", "revision_number" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_relationship_analysis_active_claim",
            table: "relationship_analysis",
            columns: new[] { "task", "created_at_utc_ticks", "pair_id" },
            filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

        migrationBuilder.CreateIndex(
            name: "IX_relationship_analysis_statement_candidate_id",
            table: "relationship_analysis",
            column: "statement_candidate_id");

        migrationBuilder.CreateIndex(
            name: "IX_relationship_analysis_status_created_at_utc_ticks_pair_id",
            table: "relationship_analysis",
            columns: new[] { "status", "created_at_utc_ticks", "pair_id" });

        migrationBuilder.CreateIndex(
            name: "IX_relationship_analysis_runs_pair_id_task_started_at_utc_ticks",
            table: "relationship_analysis_runs",
            columns: new[] { "pair_id", "task", "started_at_utc_ticks" });

        migrationBuilder.CreateIndex(
            name: "IX_relationship_analysis_runs_previous_run_id",
            table: "relationship_analysis_runs",
            column: "previous_run_id");

        migrationBuilder.CreateIndex(
            name: "IX_relationship_review_decisions_record_id_review_state_version",
            table: "relationship_review_decisions",
            columns: new[] { "record_id", "review_state_version" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_relationship_review_decisions_record_id_revision_number",
            table: "relationship_review_decisions",
            columns: new[] { "record_id", "revision_number" });

        migrationBuilder.CreateIndex(
            name: "IX_relationship_revisions_run_id",
            table: "relationship_revisions",
            column: "run_id",
            unique: true);

        migrationBuilder.Sql("""
            CREATE TRIGGER trg_relationship_analysis_notify
            AFTER INSERT OR UPDATE OF status, retry_at ON relationship_analysis
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "relationship_analysis_budget");

        migrationBuilder.DropTable(
            name: "relationship_review_decisions");

        migrationBuilder.DropTable(
            name: "relationship_revisions");

        migrationBuilder.DropTable(
            name: "relationship_analysis_runs");

        migrationBuilder.DropTable(
            name: "relationship_drafts");

        migrationBuilder.DropTable(
            name: "relationship_analysis");
    }
}
