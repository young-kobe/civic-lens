using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012190000_StatementGate")]
public partial class StatementGate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "statement_analysis_budget",
            columns: table => new
            {
                day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                charged_tokens = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_analysis_budget", x => new { x.day_utc, x.task });
                table.CheckConstraint("ck_statement_analysis_budget_charge", "charged_tokens >= 0");
            });

        migrationBuilder.CreateTable(
            name: "statement_analysis_runs",
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
                issue_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                quotes_json = table.Column<string>(type: "text", nullable: true),
                validation_json = table.Column<string>(type: "text", nullable: true),
                context_json = table.Column<string>(type: "text", nullable: true),
                started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_analysis_runs", x => x.run_id);
                table.CheckConstraint("ck_statement_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (issue_revision_id IS NULL OR issue_revision_id ~ '^[0-9a-f]{64}$') AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.CheckConstraint("ck_statement_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Classified','Bypassed')");
                table.CheckConstraint("ck_statement_analysis_runs_reason", "reason IS NULL OR reason IN ('passed','no-tracked-issue','over-byte-cap','gate-failed-open')");
                table.CheckConstraint("ck_statement_analysis_runs_revision", "issue_revision_id IS NOT NULL OR outcome = 'Interrupted'");
                table.CheckConstraint("ck_statement_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                table.CheckConstraint("ck_statement_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                table.ForeignKey(
                    name: "FK_statement_analysis_runs_candidate_task",
                    columns: x => new { x.candidate_id, x.task },
                    principalTable: "statement_analysis",
                    principalColumns: new[] { "candidate_id", "task" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_statement_analysis_runs_previous_run",
                    column: x => x.previous_run_id,
                    principalTable: "statement_analysis_runs",
                    principalColumn: "run_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_statement_analysis_runs_candidate_task_started",
            table: "statement_analysis_runs",
            columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

        migrationBuilder.CreateIndex(
            name: "IX_statement_analysis_runs_previous_run_id",
            table: "statement_analysis_runs",
            column: "previous_run_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "statement_analysis_runs");
        migrationBuilder.DropTable(name: "statement_analysis_budget");
    }
}
