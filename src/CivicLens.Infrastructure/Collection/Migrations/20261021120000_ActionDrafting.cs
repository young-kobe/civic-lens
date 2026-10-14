using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261021120000_ActionDrafting")]
public partial class ActionDrafting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "includes_summaries",
            table: "action_candidates",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.DropIndex(
            name: "ux_action_candidates_detail_job",
            table: "action_candidates");

        migrationBuilder.CreateIndex(
            name: "ux_action_candidates_detail_job",
            table: "action_candidates",
            columns: new[] { "detail_job_id", "includes_summaries" },
            unique: true);

        migrationBuilder.Sql("""
            INSERT INTO action_candidates (candidate_id, source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id,
                configuration_revision_id, created_at_utc_ticks, includes_summaries)
            SELECT encode(sha256(convert_to(length(source_id) || ':' || source_id || congress || ':' || bill_type || ':' || bill_number || ':' ||
                    update_date_utc_ticks || ':summaries', 'UTF8')), 'hex'),
                source_id, congress, bill_type, bill_number, update_date_utc_ticks, detail_job_id, configuration_revision_id,
                summaries_completed_at_utc_ticks, true
            FROM action_candidates
            WHERE summaries_completed_at_utc_ticks IS NOT NULL;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_action_candidates_summaries",
            table: "action_candidates");

        migrationBuilder.DropColumn(
            name: "summaries_completed_at_utc_ticks",
            table: "action_candidates");

        migrationBuilder.CreateTable(
            name: "action_analysis",
            columns: table => new
            {
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                table.PrimaryKey("PK_action_analysis", x => new { x.candidate_id, x.task });
                table.CheckConstraint("ck_action_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                table.CheckConstraint("ck_action_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                table.CheckConstraint("ck_action_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                table.CheckConstraint("ck_action_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_action_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                table.CheckConstraint("ck_action_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                table.ForeignKey(
                    name: "FK_action_analysis_action_candidates_candidate_id",
                    column: x => x.candidate_id,
                    principalTable: "action_candidates",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "action_analysis_budget",
            columns: table => new
            {
                day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                charged_tokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_action_analysis_budget", x => new { x.day_utc, x.task });
                table.CheckConstraint("ck_action_analysis_budget_charge", "charged_tokens >= 0");
            });

        migrationBuilder.CreateTable(
            name: "action_drafts",
            columns: table => new
            {
                record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                congress = table.Column<int>(type: "integer", nullable: false),
                bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                bill_number = table.Column<int>(type: "integer", nullable: false),
                current_revision_number = table.Column<int>(type: "integer", nullable: false),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_action_drafts", x => x.record_id);
                table.CheckConstraint("ck_action_drafts_bill", "congress >= 1 AND bill_number >= 1");
                table.CheckConstraint("ck_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                table.CheckConstraint("ck_action_drafts_revision", "current_revision_number >= 1");
            });

        migrationBuilder.CreateTable(
            name: "action_analysis_runs",
            columns: table => new
            {
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                table.PrimaryKey("PK_action_analysis_runs", x => x.run_id);
                table.CheckConstraint("ck_action_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.CheckConstraint("ck_action_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                table.CheckConstraint("ck_action_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted')");
                table.CheckConstraint("ck_action_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                table.CheckConstraint("ck_action_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                table.ForeignKey(
                    name: "FK_action_analysis_runs_action_analysis_candidate_id_task",
                    columns: x => new { x.candidate_id, x.task },
                    principalTable: "action_analysis",
                    principalColumns: new[] { "candidate_id", "task" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_action_analysis_runs_action_analysis_runs_previous_run_id",
                    column: x => x.previous_run_id,
                    principalTable: "action_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "action_revisions",
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
                table.PrimaryKey("PK_action_revisions", x => new { x.record_id, x.revision_number });
                table.CheckConstraint("ck_action_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                table.CheckConstraint("ck_action_revisions_number", "revision_number >= 1");
                table.CheckConstraint("ck_action_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_action_revisions_action_analysis_runs_run_id",
                    column: x => x.run_id,
                    principalTable: "action_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_action_revisions_action_drafts_record_id",
                    column: x => x.record_id,
                    principalTable: "action_drafts",
                    principalColumn: "record_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_action_analysis_status_created_at_utc_ticks_candidate_id",
            table: "action_analysis",
            columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

        migrationBuilder.CreateIndex(
            name: "IX_action_analysis_runs_candidate_id_task_started_at_utc_ticks",
            table: "action_analysis_runs",
            columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

        migrationBuilder.CreateIndex(
            name: "IX_action_analysis_runs_previous_run_id",
            table: "action_analysis_runs",
            column: "previous_run_id");

        migrationBuilder.CreateIndex(
            name: "ux_action_drafts_bill",
            table: "action_drafts",
            columns: new[] { "source_id", "congress", "bill_type", "bill_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_action_revisions_run_id",
            table: "action_revisions",
            column: "run_id",
            unique: true);

        migrationBuilder.Sql("""
            INSERT INTO action_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
            SELECT candidate_id, 'action-draft', 'Pending', 0, 0, 0, created_at_utc_ticks FROM action_candidates;

            CREATE TRIGGER trg_action_analysis_notify
            AFTER INSERT OR UPDATE OF status, retry_at ON action_analysis
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "action_analysis_budget");

        migrationBuilder.DropTable(
            name: "action_revisions");

        migrationBuilder.DropTable(
            name: "action_analysis_runs");

        migrationBuilder.DropTable(
            name: "action_drafts");

        migrationBuilder.DropTable(
            name: "action_analysis");

        migrationBuilder.AddColumn<long>(
            name: "summaries_completed_at_utc_ticks",
            table: "action_candidates",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_action_candidates_summaries",
            table: "action_candidates",
            sql: "summaries_completed_at_utc_ticks IS NULL OR summaries_completed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

        migrationBuilder.Sql("""
            UPDATE action_candidates earlier SET summaries_completed_at_utc_ticks = later.created_at_utc_ticks
            FROM action_candidates later
            WHERE later.detail_job_id = earlier.detail_job_id AND later.includes_summaries AND NOT earlier.includes_summaries;

            DELETE FROM action_candidates later
            WHERE later.includes_summaries AND EXISTS (
                SELECT 1 FROM action_candidates earlier WHERE earlier.detail_job_id = later.detail_job_id AND NOT earlier.includes_summaries);

            UPDATE action_candidates SET summaries_completed_at_utc_ticks = created_at_utc_ticks,
                candidate_id = encode(sha256(convert_to(length(source_id) || ':' || source_id || congress || ':' || bill_type || ':' ||
                    bill_number || ':' || update_date_utc_ticks, 'UTF8')), 'hex')
            WHERE includes_summaries;
            """);

        migrationBuilder.DropIndex(
            name: "ux_action_candidates_detail_job",
            table: "action_candidates");

        migrationBuilder.DropColumn(
            name: "includes_summaries",
            table: "action_candidates");

        migrationBuilder.CreateIndex(
            name: "ux_action_candidates_detail_job",
            table: "action_candidates",
            column: "detail_job_id",
            unique: true);
    }
}
