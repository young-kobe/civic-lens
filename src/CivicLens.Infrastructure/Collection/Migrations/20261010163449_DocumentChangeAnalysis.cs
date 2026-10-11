using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261010163449_DocumentChangeAnalysis")]
public partial class DocumentChangeAnalysis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "document_change_analysis",
            columns: table => new
            {
                comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_change_analysis", x => new { x.comparison_id, x.task });
                table.CheckConstraint("ck_document_change_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                table.CheckConstraint("ck_document_change_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                table.CheckConstraint("ck_document_change_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_document_change_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                table.CheckConstraint("ck_document_change_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                table.CheckConstraint("ck_document_change_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                table.ForeignKey(
                    name: "FK_document_change_analysis_document_change_drafts_draft_id",
                    column: x => x.draft_id,
                    principalTable: "document_change_drafts",
                    principalColumn: "draft_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_document_change_analysis_document_comparisons_comparison_id",
                    column: x => x.comparison_id,
                    principalTable: "document_comparisons",
                    principalColumn: "comparison_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "document_change_analysis_budget",
            columns: table => new
            {
                day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                charged_tokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_change_analysis_budget", x => x.day_utc);
                table.CheckConstraint("ck_document_change_analysis_budget_charge", "charged_tokens >= 0");
            });

        migrationBuilder.CreateTable(
            name: "document_change_analysis_runs",
            columns: table => new
            {
                run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                task_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                prompt_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                schema_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                previous_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                revision_number = table.Column<int>(type: "integer", nullable: true),
                started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_change_analysis_runs", x => x.run_id);
                table.CheckConstraint("ck_document_change_analysis_runs_draft", "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number = 1)");
                table.CheckConstraint("ck_document_change_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.CheckConstraint("ck_document_change_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted')");
                table.CheckConstraint("ck_document_change_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                table.CheckConstraint("ck_document_change_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                table.ForeignKey(
                    name: "FK_document_change_analysis_runs_document_change_analysis_comp~",
                    columns: x => new { x.comparison_id, x.task },
                    principalTable: "document_change_analysis",
                    principalColumns: new[] { "comparison_id", "task" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_document_change_analysis_runs_document_change_analysis_runs~",
                    column: x => x.previous_run_id,
                    principalTable: "document_change_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_document_change_analysis_runs_document_change_revisions_dra~",
                    columns: x => new { x.draft_id, x.revision_number },
                    principalTable: "document_change_revisions",
                    principalColumns: new[] { "draft_id", "revision_number" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_document_change_analysis_draft_id",
            table: "document_change_analysis",
            column: "draft_id");

        migrationBuilder.CreateIndex(
            name: "IX_document_change_analysis_status_created_at_utc_ticks_compar~",
            table: "document_change_analysis",
            columns: new[] { "status", "created_at_utc_ticks", "comparison_id" });

        migrationBuilder.CreateIndex(
            name: "IX_document_change_analysis_runs_comparison_id_task_started_at~",
            table: "document_change_analysis_runs",
            columns: new[] { "comparison_id", "task", "started_at_utc_ticks" });

        migrationBuilder.CreateIndex(
            name: "IX_document_change_analysis_runs_draft_id_revision_number",
            table: "document_change_analysis_runs",
            columns: new[] { "draft_id", "revision_number" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_document_change_analysis_runs_previous_run_id",
            table: "document_change_analysis_runs",
            column: "previous_run_id");

        migrationBuilder.CreateTable(
            name: "document_change_analysis_status",
            columns: table => new
            {
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                daily_token_limit = table.Column<long>(type: "bigint", nullable: true),
                recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                paused_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                paused_until_utc_ticks = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_change_analysis_status", x => x.task);
                table.CheckConstraint("ck_document_change_analysis_status_limit", "daily_token_limit IS NULL OR daily_token_limit > 0");
                table.CheckConstraint("ck_document_change_analysis_status_pause", "paused_until_utc_ticks IS NULL OR paused_reason IS NOT NULL");
            });

        migrationBuilder.Sql("""
            CREATE FUNCTION civic_lens_register_document_change_analysis() RETURNS trigger AS $$
            BEGIN
                IF (NEW.result_json::jsonb ->> 'status') = '0' AND jsonb_array_length(NEW.result_json::jsonb -> 'hunks') > 0 THEN
                    INSERT INTO document_change_analysis (comparison_id, task, status, fence, attempts,
                        reserved_tokens, created_at_utc_ticks)
                    VALUES (NEW.comparison_id, 'draft', 'Pending', 0, 0, 0,
                        (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
                    ON CONFLICT (comparison_id, task) DO NOTHING;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_document_comparisons_register_analysis
            AFTER INSERT ON document_comparisons
            FOR EACH ROW EXECUTE FUNCTION civic_lens_register_document_change_analysis();

            INSERT INTO document_change_analysis (comparison_id, task, status, fence, attempts,
                reserved_tokens, created_at_utc_ticks)
            SELECT c.comparison_id, 'draft', 'Pending', 0, 0, 0,
                (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint
              FROM document_comparisons c
             WHERE (c.result_json::jsonb ->> 'status') = '0' AND jsonb_array_length(c.result_json::jsonb -> 'hunks') > 0
               AND NOT EXISTS (SELECT 1 FROM document_change_drafts d WHERE d.comparison_id = c.comparison_id)
            ON CONFLICT (comparison_id, task) DO NOTHING;

            CREATE FUNCTION civic_lens_notify_analysis_change() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_notify('civic_lens_analysis', 'changed');
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_document_change_analysis_notify
            AFTER INSERT OR UPDATE OF status, retry_at ON document_change_analysis
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_document_change_analysis_notify ON document_change_analysis;
            DROP TRIGGER IF EXISTS trg_document_comparisons_register_analysis ON document_comparisons;
            DROP FUNCTION IF EXISTS civic_lens_notify_analysis_change();
            DROP FUNCTION IF EXISTS civic_lens_register_document_change_analysis();
            """);
        migrationBuilder.DropTable(
            name: "document_change_analysis_status");

        migrationBuilder.DropTable(
            name: "document_change_analysis_budget");

        migrationBuilder.DropTable(
            name: "document_change_analysis_runs");

        migrationBuilder.DropTable(
            name: "document_change_analysis");
    }
}
