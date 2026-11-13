using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ExecutiveActionSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions");

            migrationBuilder.AddColumn<string>(
                name: "run_id",
                table: "executive_action_revisions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "summary_cut",
                table: "executive_action_revisions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "summary_text_origin",
                table: "executive_action_revisions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "executive_action_revision_summary_sentences",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    sentence = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    quote_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quote_start = table.Column<int>(type: "integer", nullable: false),
                    quote_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_summary_sentences", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_summary_sentences_position", "position BETWEEN 0 AND 2");
                    table.CheckConstraint("ck_executive_action_revision_summary_sentences_quote", "quote_start >= 0 AND quote_length > 0");
                    table.CheckConstraint("ck_executive_action_revision_summary_sentences_sentence", "btrim(sentence) <> ''");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_summary_sentences_document_extrac~",
                        column: x => x.quote_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revision_summary_sentences_executive_actio~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exec_summary_analysis",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exec_summary_analysis", x => new { x.record_id, x.task });
                    table.CheckConstraint("ck_exec_summary_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_exec_summary_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_exec_summary_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_exec_summary_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_exec_summary_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_exec_summary_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_exec_summary_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_exec_summary_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                    table.CheckConstraint("ck_exec_summary_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                    table.ForeignKey(
                        name: "FK_exec_summary_analysis_executive_action_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "executive_action_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exec_summary_analysis_runs",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    system_prompt_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    output_schema_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_message = table.Column<string>(type: "text", nullable: true),
                    effort = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exec_summary_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_exec_summary_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','recordRejected','revisionConflict')");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_exec_summary_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_exec_summary_analysis_runs_analysis_output_schemas_output_s~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exec_summary_analysis_runs_analysis_system_prompts_system_p~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exec_summary_analysis_runs_exec_summary_analysis_record_id_~",
                        columns: x => new { x.record_id, x.task },
                        principalTable: "exec_summary_analysis",
                        principalColumns: new[] { "record_id", "task" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_exec_summary_analysis_runs_exec_summary_analysis_runs_previ~",
                        column: x => x.previous_run_id,
                        principalTable: "exec_summary_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revisions_run_id",
                table: "executive_action_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions",
                sql: "author_kind = 'Code' AND author_subject = 'civic-lens:code' OR author_kind = 'Analysis' AND author_subject = 'civic-lens:analysis'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_run",
                table: "executive_action_revisions",
                sql: "(author_kind = 'Analysis') = (run_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_summary_analysis",
                table: "executive_action_revisions",
                sql: "author_kind <> 'Analysis' OR summary_text_origin IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_summary_origin",
                table: "executive_action_revisions",
                sql: "summary_text_origin IN ('Model')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_summary_written",
                table: "executive_action_revisions",
                sql: "(summary_text_origin IS NULL) = (summary_cut IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_exec_summary_analysis_active_claim",
                table: "exec_summary_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "record_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_retry_of_run_id",
                table: "exec_summary_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_status_created_at_utc_ticks_record_id",
                table: "exec_summary_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_runs_output_schema_sha256",
                table: "exec_summary_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_runs_previous_run_id",
                table: "exec_summary_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_runs_record_id_task_started_at_utc_ti~",
                table: "exec_summary_analysis_runs",
                columns: new[] { "record_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_exec_summary_analysis_runs_system_prompt_sha256",
                table: "exec_summary_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_summary_sentences_quote_extractio~",
                table: "executive_action_revision_summary_sentences",
                column: "quote_extraction_id");

            migrationBuilder.AddForeignKey(
                name: "FK_executive_action_revisions_exec_summary_analysis_runs_run_id",
                table: "executive_action_revisions",
                column: "run_id",
                principalTable: "exec_summary_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_exec_summary_analysis_exec_summary_analysis_runs_retry_of_r~",
                table: "exec_summary_analysis",
                column: "retry_of_run_id",
                principalTable: "exec_summary_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            // The worker wakes on a new entry. A text that exists before this migration gets its entry here, because the store adds an entry only when it saves a text.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_exec_summary_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON exec_summary_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                INSERT INTO exec_summary_analysis (record_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks, priority)
                SELECT d.record_id, 'executive-summary', 'Pending', 0, 0, 0,
                       (extract(epoch FROM clock_timestamp()) * 10000000)::bigint + 621355968000000000, 3
                FROM executive_action_texts t
                JOIN executive_action_documents d ON d.source_id = t.source_id AND d.document_number = t.document_number
                WHERE d.notice_url IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_executive_action_revisions_exec_summary_analysis_runs_run_id",
                table: "executive_action_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_exec_summary_analysis_exec_summary_analysis_runs_retry_of_r~",
                table: "exec_summary_analysis");

            migrationBuilder.DropTable(
                name: "executive_action_revision_summary_sentences");

            migrationBuilder.DropTable(
                name: "exec_summary_analysis_runs");

            migrationBuilder.DropTable(
                name: "exec_summary_analysis");

            migrationBuilder.DropIndex(
                name: "IX_executive_action_revisions_run_id",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_run",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_summary_analysis",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_summary_origin",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_summary_written",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_cut",
                table: "executive_action_revisions");

            migrationBuilder.DropColumn(
                name: "summary_text_origin",
                table: "executive_action_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_author",
                table: "executive_action_revisions",
                sql: "author_kind = 'Code' AND author_subject = 'civic-lens:code'");
        }
    }
}
