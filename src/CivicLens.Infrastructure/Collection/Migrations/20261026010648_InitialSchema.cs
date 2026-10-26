using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_drafts", x => x.record_id);
                    table.CheckConstraint("ck_action_drafts_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_action_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_action_drafts_revision", "current_revision_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "bill_stream_cursors",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_processed_day = table.Column<DateOnly>(type: "date", nullable: true),
                    pass_admitted_bills = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_stream_cursors", x => x.source_id);
                    table.CheckConstraint("ck_bill_stream_cursors_pass_admitted_bills", "pass_admitted_bills >= 0");
                    table.CheckConstraint("ck_bill_stream_cursors_updated", "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                });

            migrationBuilder.CreateTable(
                name: "brief_analysis",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("PK_brief_analysis", x => new { x.record_id, x.task });
                    table.CheckConstraint("ck_brief_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_brief_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_brief_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_brief_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_brief_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_brief_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                    table.CheckConstraint("ck_brief_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                    table.CheckConstraint("ck_brief_analysis_week", "EXTRACT(ISODOW FROM week_start) = 1");
                });

            migrationBuilder.CreateTable(
                name: "brief_analysis_budget",
                columns: table => new
                {
                    day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    charged_tokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_analysis_budget", x => new { x.day_utc, x.task });
                    table.CheckConstraint("ck_brief_analysis_budget_charge", "charged_tokens >= 0");
                });

            migrationBuilder.CreateTable(
                name: "brief_drafts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_drafts", x => x.record_id);
                    table.CheckConstraint("ck_brief_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_brief_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_brief_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_brief_drafts_revision", "current_revision_number >= 1");
                    table.CheckConstraint("ck_brief_drafts_week", "EXTRACT(ISODOW FROM week_start) = 1");
                });

            migrationBuilder.CreateTable(
                name: "brief_schedule",
                columns: table => new
                {
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_week_start = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_schedule", x => x.task);
                    table.CheckConstraint("ck_brief_schedule_week", "EXTRACT(ISODOW FROM first_week_start) = 1");
                });

            migrationBuilder.CreateTable(
                name: "collection_captures",
                columns: table => new
                {
                    sha256 = table.Column<string>(type: "text", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_captures", x => x.sha256);
                    table.CheckConstraint("ck_collection_captures_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_collection_captures_sha256", "sha256 ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "collection_configuration_revisions",
                columns: table => new
                {
                    revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_configuration_revisions", x => x.revision_id);
                    table.CheckConstraint("ck_collection_configuration_revisions_id", "revision_id ~ '^[0-9a-f]{64}$'");
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
                name: "evidence_processing",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    requested_url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    observed_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    stage = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    lease_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    fence = table.Column<long>(type: "bigint", nullable: false),
                    lease_expires_at = table.Column<long>(type: "bigint", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    retry_at = table.Column<long>(type: "bigint", nullable: true),
                    predecessor_attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    outcome = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    admitted_count = table.Column<int>(type: "integer", nullable: false),
                    deferred_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_processing", x => new { x.job_id, x.attempt_id });
                    table.CheckConstraint("ck_evidence_processing_counts", "admitted_count >= 0 AND deferred_count >= 0 AND duplicate_count >= 0");
                    table.CheckConstraint("ck_evidence_processing_evidence_ids", "(extraction_id IS NULL OR extraction_id ~ '^[0-9a-f]{64}$') AND (comparison_id IS NULL OR comparison_id ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_evidence_processing_fence", "fence >= 0 AND attempts >= 0");
                    table.CheckConstraint("ck_evidence_processing_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_evidence_processing_stage", "stage IN ('Preparation','Extraction','Comparison','Complete')");
                    table.CheckConstraint("ck_evidence_processing_status", "status IN ('Pending','Running','RetryWaiting','WaitingForPredecessor','Succeeded','Blocked','Failed')");
                });

            migrationBuilder.CreateTable(
                name: "publication_releases",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    directory_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    record_count = table.Column<int>(type: "integer", nullable: false),
                    records_json = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_releases", x => x.release_number);
                    table.CheckConstraint("ck_publication_releases_directory", "directory_name ~ '^[0-9]{6}-[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_publication_releases_number", "release_number BETWEEN 1 AND 999999");
                    table.CheckConstraint("ck_publication_releases_published", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                });

            migrationBuilder.CreateTable(
                name: "reader_report_cursor",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    cursor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reader_report_cursor", x => x.id);
                    table.CheckConstraint("ck_reader_report_cursor_id", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "reader_reports",
                columns: table => new
                {
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    received_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resolved_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reader_reports", x => x.report_id);
                    table.CheckConstraint("ck_reader_reports_received", "received_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_reader_reports_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
                    table.CheckConstraint("ck_reader_reports_resolution", "(status = 'Open') = (resolved_by IS NULL AND resolved_at_utc_ticks IS NULL) AND (status <> 'CorrectionNeeded' OR note IS NOT NULL)");
                    table.CheckConstraint("ck_reader_reports_revision", "revision_number >= 1");
                    table.CheckConstraint("ck_reader_reports_status", "status IN ('Open','Dismissed','CorrectionNeeded')");
                    table.CheckConstraint("ck_reader_reports_version", "version >= 0");
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
                name: "vote_analysis_budget",
                columns: table => new
                {
                    day_utc = table.Column<DateOnly>(type: "date", nullable: false),
                    task = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    charged_tokens = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_analysis_budget", x => new { x.day_utc, x.task });
                    table.CheckConstraint("ck_vote_analysis_budget_charge", "charged_tokens >= 0");
                });

            migrationBuilder.CreateTable(
                name: "vote_drafts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_call_number = table.Column<int>(type: "integer", nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: ""),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_drafts", x => x.record_id);
                    table.CheckConstraint("ck_vote_drafts_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_vote_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_vote_drafts_revision", "current_revision_number >= 1");
                    table.CheckConstraint("ck_vote_drafts_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                });

            migrationBuilder.CreateTable(
                name: "brief_analysis_runs",
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
                    started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_brief_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_brief_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_brief_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noNewRecords')");
                    table.CheckConstraint("ck_brief_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_brief_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.ForeignKey(
                        name: "FK_brief_analysis_runs_brief_analysis_record_id_task",
                        columns: x => new { x.record_id, x.task },
                        principalTable: "brief_analysis",
                        principalColumns: new[] { "record_id", "task" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_analysis_runs_brief_analysis_runs_previous_run_id",
                        column: x => x.previous_run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    requested_url = table.Column<string>(type: "text", nullable: false),
                    final_url = table.Column<string>(type: "text", nullable: false),
                    observed_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    has_response = table.Column<bool>(type: "boolean", nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_etag = table.Column<string>(type: "text", nullable: true),
                    response_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    response_content_type = table.Column<string>(type: "text", nullable: true),
                    response_content_encodings = table.Column<string[]>(type: "text[]", nullable: true),
                    has_sent_validators = table.Column<bool>(type: "boolean", nullable: false),
                    sent_etag = table.Column<string>(type: "text", nullable: true),
                    sent_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    robots_crawl_delay_milliseconds = table.Column<long>(type: "bigint", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    retry_delay_ticks = table.Column<long>(type: "bigint", nullable: true),
                    capture_sha256 = table.Column<string>(type: "text", nullable: true),
                    prior_capture_attempt_id = table.Column<string>(type: "text", nullable: true),
                    prior_capture_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_collection_attempts_identity", "length(btrim(attempt_id)) > 0 AND length(btrim(source_id)) > 0 AND length(btrim(requested_url)) > 0 AND length(btrim(final_url)) > 0");
                    table.CheckConstraint("ck_collection_attempts_observed_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_outcome", "(outcome = 'captured' AND has_response AND response_status_code IS NOT NULL AND response_status_code = 200 AND capture_sha256 IS NOT NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'notModified' AND has_response AND has_sent_validators AND response_status_code IS NOT NULL AND response_status_code = 304 AND capture_sha256 IS NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL)\nOR (outcome = 'failed' AND failure_code IS NOT NULL AND retry_delay_ticks IS NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'deferred' AND failure_code IS NOT NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_prior_outcome", "prior_capture_attempt_id IS NULL OR (outcome = 'notModified' AND prior_capture_attempt_id <> attempt_id)");
                    table.CheckConstraint("ck_collection_attempts_prior_pair", "(prior_capture_attempt_id IS NULL) = (prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response", "(has_response AND response_status_code IS NOT NULL AND response_status_code BETWEEN 100 AND 599 AND response_content_encodings IS NOT NULL AND array_position(response_content_encodings, NULL) IS NULL)\nOR (NOT has_response AND response_status_code IS NULL AND response_etag IS NULL AND response_last_modified_utc_ticks IS NULL AND response_content_type IS NULL AND response_content_encodings IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response_ticks", "response_last_modified_utc_ticks IS NULL OR response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
                    table.CheckConstraint("ck_collection_attempts_robots_delay", "robots_crawl_delay_milliseconds IS NULL OR robots_crawl_delay_milliseconds BETWEEN 0 AND 922337203685000");
                    table.CheckConstraint("ck_collection_attempts_sent_ticks", "sent_last_modified_utc_ticks IS NULL OR sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_validators", "(has_sent_validators OR (sent_etag IS NULL AND sent_last_modified_utc_ticks IS NULL))");
                    table.ForeignKey(
                        name: "FK_collection_attempts_collection_attempts_prior_capture_attem~",
                        column: x => x.prior_capture_attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_attempts_collection_captures_capture_sha256",
                        column: x => x.capture_sha256,
                        principalTable: "collection_captures",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_jobs",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition_json = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    is_root = table.Column<bool>(type: "boolean", nullable: false),
                    configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pacing_key = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    retry_at = table.Column<long>(type: "bigint", nullable: true),
                    cancellation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    lease_token = table.Column<string>(type: "text", nullable: true),
                    lease_fence = table.Column<long>(type: "bigint", nullable: false),
                    lease_expires_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_jobs", x => x.job_id);
                    table.CheckConstraint("ck_job_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_job_state", "state IN ('Pending','Running','WaitingToRetry','Succeeded','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "fk_collection_jobs_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_source_schedules",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    next_due_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_source_schedules", x => x.source_id);
                    table.CheckConstraint("ck_collection_source_schedules_due", "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_source_schedules_interval", "interval_seconds BETWEEN 60 AND 604800");
                    table.CheckConstraint("ck_collection_source_schedules_revision", "configuration_revision_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_collection_source_schedules_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revisions",
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
                    table.PrimaryKey("PK_brief_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_brief_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_brief_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_brief_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "brief_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_discoveries",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    discovery_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_discoveries", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_collection_discoveries_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_extractions",
                columns: table => new
                {
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    extraction_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    profile_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_extractions", x => x.extraction_id);
                    table.CheckConstraint("ck_document_extractions_hashes", "extraction_id ~ '^[0-9a-f]{64}$' AND text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_document_extractions_text_limit", "length(text) <= 2000000");
                    table.CheckConstraint("ck_document_extractions_versions", "length(btrim(extraction_version)) > 0");
                    table.ForeignKey(
                        name: "FK_document_extractions_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_snapshots",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_snapshots", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_roster_snapshots_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_gates",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bill_number = table.Column<int>(type: "integer", nullable: false),
                    update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    policy_area = table.Column<string>(type: "text", nullable: true),
                    cosponsor_count = table.Column<int>(type: "integer", nullable: false),
                    summary_count = table.Column<int>(type: "integer", nullable: false),
                    issue_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    has_tracked_issue = table.Column<bool>(type: "boolean", nullable: false),
                    decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    decided_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_gates", x => x.detail_job_id);
                    table.CheckConstraint("ck_bill_gates_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_bill_gates_cosponsor_count", "cosponsor_count >= 0");
                    table.CheckConstraint("ck_bill_gates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_bill_gates_decided", "(decision = 'Pending') = (decided_at_utc_ticks IS NULL) AND (decided_at_utc_ticks IS NULL OR decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999)");
                    table.CheckConstraint("ck_bill_gates_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_bill_gates_reason", "(decision = 'Pending' AND reason IS NULL) OR (decision = 'Passed' AND reason IS NOT NULL AND reason IN ('CoveredSponsor','TrackedPolicyArea','CoveredCosponsor')) OR (decision = 'NotRelevant' AND reason IS NOT NULL AND reason IN ('NoPolicyArea','UntrackedPolicyArea'))");
                    table.CheckConstraint("ck_bill_gates_summary_count", "summary_count >= 0");
                    table.CheckConstraint("ck_bill_gates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_bill_gates_collection_jobs_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_stream_pages",
                columns: table => new
                {
                    list_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    unknown_type_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_stream_pages", x => x.list_job_id);
                    table.CheckConstraint("ck_bill_stream_pages_recorded", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_bill_stream_pages_unknown_types", "unknown_type_count >= 0");
                    table.ForeignKey(
                        name: "FK_bill_stream_pages_collection_jobs_list_job_id",
                        column: x => x.list_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_candidate_jobs",
                columns: table => new
                {
                    candidate_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    job_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_candidate_jobs", x => x.candidate_hash);
                    table.ForeignKey(
                        name: "FK_collection_candidate_jobs_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_job_attempts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    request_json = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    resolution_json = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_job_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_job_attempt_resolution", "(resolution_json IS NULL) = (completed_at IS NULL)");
                    table.CheckConstraint("ck_job_attempt_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "FK_collection_job_attempts_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_review_decisions",
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
                    table.PrimaryKey("PK_brief_review_decisions", x => x.decision_id);
                    table.CheckConstraint("ck_brief_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_brief_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_brief_review_decisions_brief_revisions_record_id_revision_n~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_admission_batches",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    attempt_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_json = table.Column<string>(type: "text", nullable: false),
                    configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_admission_batches", x => x.idempotency_key);
                    table.ForeignKey(
                        name: "FK_collection_admission_batches_collection_discoveries_attempt~",
                        column: x => x.attempt_id,
                        principalTable: "collection_discoveries",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_collection_admission_batches_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_comparisons",
                columns: table => new
                {
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    after_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    algorithm_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    settings_version = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: false),
                    has_changes = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_comparisons", x => x.comparison_id);
                    table.CheckConstraint("ck_document_comparisons_id", "comparison_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_document_comparisons_document_extractions_after_extraction_~",
                        column: x => x.after_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_comparisons_document_extractions_before_extraction~",
                        column: x => x.before_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_candidates",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_candidates", x => x.candidate_id);
                    table.CheckConstraint("ck_statement_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_statement_candidates_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_candidates_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_versions",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_call_number = table.Column<int>(type: "integer", nullable: false),
                    update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bill_number = table.Column<int>(type: "integer", nullable: true),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_versions", x => x.detail_job_id);
                    table.CheckConstraint("ck_vote_versions_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
                    table.CheckConstraint("ck_vote_versions_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_versions_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.CheckConstraint("ck_vote_versions_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_vote_versions_collection_jobs_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_versions_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_officials",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    govtrack_id = table.Column<int>(type: "integer", nullable: false),
                    wikidata_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_officials", x => new { x.attempt_id, x.govtrack_id });
                    table.CheckConstraint("ck_executive_officials_identity", "govtrack_id > 0 AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND length(btrim(full_name)) > 0");
                    table.ForeignKey(
                        name: "FK_executive_officials_roster_snapshots_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "roster_snapshots",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_members",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    lis_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    office = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_members", x => new { x.attempt_id, x.bioguide_id });
                    table.CheckConstraint("ck_roster_members_identity", "bioguide_id ~ '^[A-Z][0-9]{6}$' AND length(btrim(full_name)) > 0");
                    table.ForeignKey(
                        name: "FK_roster_members_roster_snapshots_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "roster_snapshots",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_candidates",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bill_number = table.Column<int>(type: "integer", nullable: false),
                    update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    includes_summaries = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_candidates", x => x.candidate_id);
                    table.CheckConstraint("ck_action_candidates_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_action_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_action_candidates_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_action_candidates_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_follow_on_jobs",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_follow_on_jobs", x => x.job_id);
                    table.ForeignKey(
                        name: "FK_bill_follow_on_jobs_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_follow_on_jobs_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_pages",
                columns: table => new
                {
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    page_order = table.Column<int>(type: "integer", nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    closes_chain = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_pages", x => x.job_id);
                    table.CheckConstraint("ck_bill_pages_detail", "role <> 'Detail' OR (job_id = detail_job_id AND page_order = 0 AND closes_chain)");
                    table.CheckConstraint("ck_bill_pages_order", "page_order >= 0");
                    table.CheckConstraint("ck_bill_pages_role", "role IN ('Detail','Cosponsors','Actions','Summaries')");
                    table.ForeignKey(
                        name: "FK_bill_pages_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_pages_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_pages_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_collector_slot",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    attempt_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    token = table.Column<string>(type: "text", nullable: true),
                    fence = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_collector_slot", x => x.id);
                    table.CheckConstraint("ck_collector_lease", "(token IS NULL AND expires_at IS NULL AND job_id IS NULL AND attempt_id IS NULL) OR (token IS NOT NULL AND expires_at IS NOT NULL AND job_id IS NOT NULL AND attempt_id IS NOT NULL)");
                    table.CheckConstraint("ck_collector_singleton", "id = 1");
                    table.ForeignKey(
                        name: "FK_collection_collector_slot_collection_job_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_job_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_collection_collector_slot_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_job_origins",
                columns: table => new
                {
                    origin = table.Column<string>(type: "text", nullable: false),
                    not_before = table.Column<long>(type: "bigint", nullable: false),
                    unresolved_attempt_id = table.Column<string>(type: "character varying(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_job_origins", x => x.origin);
                    table.ForeignKey(
                        name: "FK_collection_job_origins_collection_job_attempts_unresolved_a~",
                        column: x => x.unresolved_attempt_id,
                        principalTable: "collection_job_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_drafts",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_drafts", x => x.draft_id);
                    table.CheckConstraint("ck_document_change_drafts_id", "draft_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_document_change_drafts_versions", "current_revision_number >= 1 AND review_state_version >= 0");
                    table.ForeignKey(
                        name: "FK_document_change_drafts_document_comparisons_comparison_id",
                        column: x => x.comparison_id,
                        principalTable: "document_comparisons",
                        principalColumn: "comparison_id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_analysis", x => new { x.pair_id, x.task });
                    table.CheckConstraint("ck_relationship_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_relationship_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_relationship_analysis_pair", "pair_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_relationship_analysis_priority", "priority BETWEEN 1 AND 3");
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
                name: "statement_analysis",
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_analysis", x => new { x.candidate_id, x.task });
                    table.CheckConstraint("ck_statement_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_statement_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_statement_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_statement_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_statement_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_statement_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                    table.CheckConstraint("ck_statement_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                    table.ForeignKey(
                        name: "FK_statement_analysis_statement_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "statement_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_drafts",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    review_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_drafts", x => x.candidate_id);
                    table.CheckConstraint("ck_statement_drafts_not_drafted", "(current_revision_number = 0) = (review_status = 'NotDrafted')");
                    table.CheckConstraint("ck_statement_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_statement_drafts_review_status", "review_status IN ('NotDrafted','AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
                    table.CheckConstraint("ck_statement_drafts_revision", "current_revision_number >= 0");
                    table.ForeignKey(
                        name: "FK_statement_drafts_statement_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "statement_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_candidates",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_call_number = table.Column<int>(type: "integer", nullable: false),
                    update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bill_number = table.Column<int>(type: "integer", nullable: true),
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    members_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    detail_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    members_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    configuration_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_candidates", x => x.candidate_id);
                    table.CheckConstraint("ck_vote_candidates_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
                    table.CheckConstraint("ck_vote_candidates_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_candidates_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.CheckConstraint("ck_vote_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_vote_candidates_collection_jobs_members_job_id",
                        column: x => x.members_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_candidates_document_extractions_detail_extraction_id",
                        column: x => x.detail_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_candidates_document_extractions_members_extraction_id",
                        column: x => x.members_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_candidates_vote_versions_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "vote_versions",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_candidates_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_terms",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    govtrack_id = table.Column<int>(type: "integer", nullable: false),
                    term_index = table.Column<int>(type: "integer", nullable: false),
                    office = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    party = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_terms", x => new { x.attempt_id, x.govtrack_id, x.term_index });
                    table.CheckConstraint("ck_executive_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
                    table.CheckConstraint("ck_executive_terms_office", "office IN ('prez', 'viceprez')");
                    table.ForeignKey(
                        name: "FK_executive_terms_executive_officials_attempt_id_govtrack_id",
                        columns: x => new { x.attempt_id, x.govtrack_id },
                        principalTable: "executive_officials",
                        principalColumns: new[] { "attempt_id", "govtrack_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roster_terms",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    term_index = table.Column<int>(type: "integer", nullable: false),
                    chamber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    district = table.Column<int>(type: "integer", nullable: true),
                    senate_class = table.Column<int>(type: "integer", nullable: true),
                    party = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_terms", x => new { x.attempt_id, x.bioguide_id, x.term_index });
                    table.CheckConstraint("ck_roster_terms_chamber", "chamber IN ('sen', 'rep')");
                    table.CheckConstraint("ck_roster_terms_dates", "term_index >= 0 AND ends_on >= starts_on");
                    table.ForeignKey(
                        name: "FK_roster_terms_roster_members_attempt_id_bioguide_id",
                        columns: x => new { x.attempt_id, x.bioguide_id },
                        principalTable: "roster_members",
                        principalColumns: new[] { "attempt_id", "bioguide_id" },
                        onDelete: ReferentialAction.Restrict);
                });

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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_analysis", x => new { x.candidate_id, x.task });
                    table.CheckConstraint("ck_action_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_action_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_action_analysis_priority", "priority BETWEEN 1 AND 3");
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_analysis", x => new { x.comparison_id, x.task });
                    table.CheckConstraint("ck_document_change_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_document_change_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_document_change_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_document_change_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_document_change_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
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
                name: "document_change_revisions",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
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
                    table.CheckConstraint("ck_relationship_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noRelationship','noLongerMatches','targetRecited')");
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
                    table.CheckConstraint("ck_statement_analysis_runs_reason", "reason IS NULL OR reason IN ('passed','passed-bill-or-vote','passed-bill-number','no-tracked-issue','over-byte-cap','gate-failed-open')");
                    table.CheckConstraint("ck_statement_analysis_runs_revision", "issue_revision_id IS NOT NULL OR outcome = 'Interrupted' OR task <> 'gate'");
                    table.CheckConstraint("ck_statement_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_statement_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.ForeignKey(
                        name: "FK_statement_analysis_runs_statement_analysis_candidate_id_task",
                        columns: x => new { x.candidate_id, x.task },
                        principalTable: "statement_analysis",
                        principalColumns: new[] { "candidate_id", "task" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_analysis_runs_statement_analysis_runs_previous_ru~",
                        column: x => x.previous_run_id,
                        principalTable: "statement_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_analysis",
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_analysis", x => new { x.candidate_id, x.task });
                    table.CheckConstraint("ck_vote_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                    table.CheckConstraint("ck_vote_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_vote_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_vote_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_vote_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_vote_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                    table.CheckConstraint("ck_vote_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                    table.ForeignKey(
                        name: "FK_vote_analysis_vote_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "vote_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
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
                    table.CheckConstraint("ck_action_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged')");
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

            migrationBuilder.CreateTable(
                name: "document_change_review_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    decision_json = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
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
                name: "statement_gate_labels",
                columns: table => new
                {
                    label_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    label_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_gate_labels", x => x.label_id);
                    table.CheckConstraint("ck_statement_gate_labels_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_statement_gate_labels_id", "label_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_statement_gate_labels_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_statement_gate_labels_statement_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "statement_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_gate_labels_statement_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "statement_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revisions",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revisions", x => new { x.candidate_id, x.revision_number });
                    table.CheckConstraint("ck_statement_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_statement_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_statement_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_statement_revisions_statement_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "statement_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revisions_statement_drafts_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "statement_drafts",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_analysis_runs",
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
                    table.PrimaryKey("PK_vote_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_vote_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_vote_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_vote_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged')");
                    table.CheckConstraint("ck_vote_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_vote_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.ForeignKey(
                        name: "FK_vote_analysis_runs_vote_analysis_candidate_id_task",
                        columns: x => new { x.candidate_id, x.task },
                        principalTable: "vote_analysis",
                        principalColumns: new[] { "candidate_id", "task" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_analysis_runs_vote_analysis_runs_previous_run_id",
                        column: x => x.previous_run_id,
                        principalTable: "vote_analysis_runs",
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
                        name: "FK_statement_review_decisions_statement_revisions_candidate_id~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_revisions",
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
                    table.PrimaryKey("PK_vote_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_vote_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_vote_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_vote_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "vote_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "vote_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                        name: "FK_action_review_decisions_action_revisions_record_id_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_review_decisions",
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
                    table.PrimaryKey("PK_vote_review_decisions", x => x.decision_id);
                    table.CheckConstraint("ck_vote_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_vote_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_vote_review_decisions_vote_revisions_record_id_revision_num~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_analysis_active_claim",
                table: "action_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

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
                name: "ix_action_candidates_bill",
                table: "action_candidates",
                columns: new[] { "source_id", "congress", "bill_type", "bill_number", "update_date_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_action_candidates_configuration_revision_id",
                table: "action_candidates",
                column: "configuration_revision_id");

            migrationBuilder.CreateIndex(
                name: "ux_action_candidates_detail_job",
                table: "action_candidates",
                columns: new[] { "detail_job_id", "includes_summaries" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_issue_ids",
                table: "action_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_official_ids",
                table: "action_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ux_action_drafts_bill",
                table: "action_drafts",
                columns: new[] { "source_id", "congress", "bill_type", "bill_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decisions_record_id_review_state_version",
                table: "action_review_decisions",
                columns: new[] { "record_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decisions_record_id_revision_number",
                table: "action_review_decisions",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_run_id",
                table: "action_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bill_follow_on_jobs_detail_job_id",
                table: "bill_follow_on_jobs",
                column: "detail_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_bill_gates_bill_version",
                table: "bill_gates",
                columns: new[] { "source_id", "congress", "bill_type", "bill_number", "update_date_utc_ticks" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bill_pages_extraction_id",
                table: "bill_pages",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "ux_bill_pages_chain_order",
                table: "bill_pages",
                columns: new[] { "detail_job_id", "role", "page_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_brief_analysis_active_claim",
                table: "brief_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "record_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_status_created_at_utc_ticks_record_id",
                table: "brief_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis",
                column: "week_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_previous_run_id",
                table: "brief_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_record_id_task_started_at_utc_ticks",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "ux_brief_drafts_week",
                table: "brief_drafts",
                column: "week_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decisions_record_id_review_state_version",
                table: "brief_review_decisions",
                columns: new[] { "record_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decisions_record_id_revision_number",
                table: "brief_review_decisions",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_run_id",
                table: "brief_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_admission_batches_attempt_id",
                table: "collection_admission_batches",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_admission_batches_configuration_revision_id",
                table: "collection_admission_batches",
                column: "configuration_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_capture",
                table: "collection_attempts",
                column: "capture_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_history_url",
                table: "collection_attempts",
                column: "requested_url")
                .Annotation("Npgsql:IndexMethod", "hash");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_prior_capture",
                table: "collection_attempts",
                columns: new[] { "source_id", "observed_at_utc_ticks", "attempt_id" },
                filter: "outcome = 'captured'");

            migrationBuilder.CreateIndex(
                name: "ix_collection_attempts_prior_capture_link",
                table: "collection_attempts",
                column: "prior_capture_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ux_collection_attempts_id_capture",
                table: "collection_attempts",
                columns: new[] { "attempt_id", "capture_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_candidate_jobs_job_id",
                table: "collection_candidate_jobs",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_collector_slot_attempt_id",
                table: "collection_collector_slot",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_collector_slot_job_id",
                table: "collection_collector_slot",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_attempts_job_id",
                table: "collection_job_attempts",
                column: "job_id",
                unique: true,
                filter: "resolution_json IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_attempts_job_id_sequence",
                table: "collection_job_attempts",
                columns: new[] { "job_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_job_origins_unresolved_attempt_id",
                table: "collection_job_origins",
                column: "unresolved_attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_collection_jobs_active_created_at_job_id",
                table: "collection_jobs",
                columns: new[] { "created_at", "job_id" },
                filter: "state IN ('Pending','Running','WaitingToRetry')");

            migrationBuilder.CreateIndex(
                name: "ix_collection_jobs_active_root_source_id",
                table: "collection_jobs",
                column: "source_id",
                filter: "is_root AND state IN ('Pending','Running','WaitingToRetry')");

            migrationBuilder.CreateIndex(
                name: "IX_collection_jobs_configuration_revision_id",
                table: "collection_jobs",
                column: "configuration_revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_jobs_created_at_job_id",
                table: "collection_jobs",
                columns: new[] { "created_at", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_collection_jobs_idempotency_key",
                table: "collection_jobs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_collection_jobs_root_source_created",
                table: "collection_jobs",
                columns: new[] { "source_id", "created_at", "job_id" },
                descending: new[] { false, true, false },
                filter: "is_root");

            migrationBuilder.CreateIndex(
                name: "IX_collection_source_schedules_configuration_revision_id",
                table: "collection_source_schedules",
                column: "configuration_revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_source_schedules_enabled_next_due_utc_ticks_sour~",
                table: "collection_source_schedules",
                columns: new[] { "enabled", "next_due_utc_ticks", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_document_change_analysis_active_claim",
                table: "document_change_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "comparison_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

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

            migrationBuilder.CreateIndex(
                name: "IX_document_comparisons_after_extraction_id",
                table: "document_comparisons",
                column: "after_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_comparisons_before_extraction_id",
                table: "document_comparisons",
                column: "before_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_extractions_attempt",
                table: "document_extractions",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_attempt_id",
                table: "evidence_processing",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_job_id_stage_status",
                table: "evidence_processing",
                columns: new[] { "job_id", "stage", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_status_observed_at_utc_ticks_attempt_id~",
                table: "evidence_processing",
                columns: new[] { "status", "observed_at_utc_ticks", "attempt_id", "job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_publication_releases_directory_name",
                table: "publication_releases",
                column: "directory_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_releases_is_active",
                table: "publication_releases",
                column: "is_active",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_reader_reports_record",
                table: "reader_reports",
                column: "record_id");

            migrationBuilder.CreateIndex(
                name: "ix_reader_reports_status_received",
                table: "reader_reports",
                columns: new[] { "status", "received_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "ix_relationship_analysis_active_claim",
                table: "relationship_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "pair_id" },
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

            migrationBuilder.CreateIndex(
                name: "ix_statement_analysis_active_claim",
                table: "statement_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_status_created_at_utc_ticks_candidate_id",
                table: "statement_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_candidate_id_task_started_at_utc_ti~",
                table: "statement_analysis_runs",
                columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_previous_run_id",
                table: "statement_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_candidates_attempt_id",
                table: "statement_candidates",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_candidates_extraction_id",
                table: "statement_candidates",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_issue_ids",
                table: "statement_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_listing",
                table: "statement_drafts",
                columns: new[] { "event_on", "candidate_id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_official_ids",
                table: "statement_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_statement_gate_labels_candidate_id_created_at_utc_ticks",
                table: "statement_gate_labels",
                columns: new[] { "candidate_id", "created_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_gate_labels_run_id",
                table: "statement_gate_labels",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decisions_candidate_id_review_state_version",
                table: "statement_review_decisions",
                columns: new[] { "candidate_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decisions_candidate_id_revision_number",
                table: "statement_review_decisions",
                columns: new[] { "candidate_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_run_id",
                table: "statement_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vote_analysis_active_claim",
                table: "vote_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_status_created_at_utc_ticks_candidate_id",
                table: "vote_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_candidate_id_task_started_at_utc_ticks",
                table: "vote_analysis_runs",
                columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_previous_run_id",
                table: "vote_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_candidates_bill",
                table: "vote_candidates",
                columns: new[] { "congress", "bill_type", "bill_number" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_candidates_configuration_revision_id",
                table: "vote_candidates",
                column: "configuration_revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_candidates_detail_extraction_id",
                table: "vote_candidates",
                column: "detail_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_candidates_members_extraction_id",
                table: "vote_candidates",
                column: "members_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_candidates_members_job_id",
                table: "vote_candidates",
                column: "members_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_candidates_roll",
                table: "vote_candidates",
                columns: new[] { "source_id", "chamber", "congress", "session_number", "roll_call_number", "update_date_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates",
                column: "detail_job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_issue_ids",
                table: "vote_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_official_ids",
                table: "vote_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ux_vote_drafts_roll",
                table: "vote_drafts",
                columns: new[] { "source_id", "chamber", "congress", "session_number", "roll_call_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decisions_record_id_review_state_version",
                table: "vote_review_decisions",
                columns: new[] { "record_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decisions_record_id_revision_number",
                table: "vote_review_decisions",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_run_id",
                table: "vote_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_versions_extraction_id",
                table: "vote_versions",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_versions_roll",
                table: "vote_versions",
                columns: new[] { "source_id", "chamber", "congress", "session_number", "roll_call_number", "update_date_utc_ticks" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION civic_lens_notify_analysis_change() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('civic_lens_analysis', TG_TABLE_NAME || ':' || NEW.task);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION civic_lens_notify_work_change() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_notify('civic_lens_work', 'changed');
                    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_action_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON action_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                CREATE TRIGGER trg_brief_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON brief_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                CREATE TRIGGER trg_collection_collector_slot_notify
                AFTER INSERT OR DELETE OR UPDATE OF job_id, attempt_id ON collection_collector_slot
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_collection_job_origins_notify
                AFTER INSERT OR DELETE OR UPDATE ON collection_job_origins
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_collection_jobs_notify
                AFTER INSERT OR DELETE OR UPDATE OF state, retry_at ON collection_jobs
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_collection_source_schedules_notify
                AFTER INSERT OR DELETE OR UPDATE OF enabled, configuration_revision_id, interval_seconds, next_due_utc_ticks ON collection_source_schedules
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_document_change_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON document_change_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                CREATE TRIGGER trg_document_extractions_notify
                AFTER INSERT ON document_extractions
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_evidence_processing_notify
                AFTER INSERT OR DELETE OR UPDATE OF status, retry_at ON evidence_processing
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();

                CREATE TRIGGER trg_relationship_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON relationship_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                CREATE TRIGGER trg_statement_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON statement_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();

                CREATE TRIGGER trg_vote_analysis_notify
                AFTER INSERT OR UPDATE OF status, retry_at ON vote_analysis
                FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE collection_attempts
                ADD CONSTRAINT fk_collection_attempts_prior_capture_observation
                FOREIGN KEY (prior_capture_attempt_id, prior_capture_sha256)
                REFERENCES collection_attempts (attempt_id, capture_sha256)
                ON DELETE RESTRICT
                """);

            migrationBuilder.Sql("""
                CREATE INDEX ix_action_drafts_approved_created ON action_drafts (created_at_utc_ticks DESC, record_id DESC) WHERE review_status = 'Approved';
                CREATE INDEX ix_collection_jobs_created_desc ON collection_jobs (created_at DESC, job_id);
                CREATE INDEX ix_document_change_decisions_created ON document_change_review_decisions (created_at_utc_ticks DESC);
                CREATE INDEX ix_document_change_drafts_approved_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status = 'Approved';
                CREATE INDEX ix_document_change_drafts_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC);
                CREATE INDEX ix_document_change_drafts_needs_action_created ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status <> 'Approved';
                CREATE INDEX ix_document_change_revisions_created ON document_change_revisions (created_at_utc_ticks DESC) WHERE revision_number > 1;
                CREATE INDEX ix_document_comparisons_has_changes ON document_comparisons (comparison_id) WHERE has_changes;
                CREATE INDEX ix_evidence_processing_changed ON evidence_processing (observed_at_utc_ticks DESC) WHERE outcome = 'Changed';
                CREATE INDEX ix_statement_drafts_approved_created ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status = 'Approved';
                CREATE INDEX ix_statement_drafts_needs_action_created ON statement_drafts (created_at_utc_ticks DESC, candidate_id DESC) WHERE review_status <> 'Approved';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "action_analysis_budget");

            migrationBuilder.DropTable(
                name: "action_review_decisions");

            migrationBuilder.DropTable(
                name: "bill_follow_on_jobs");

            migrationBuilder.DropTable(
                name: "bill_pages");

            migrationBuilder.DropTable(
                name: "bill_stream_cursors");

            migrationBuilder.DropTable(
                name: "bill_stream_pages");

            migrationBuilder.DropTable(
                name: "brief_analysis_budget");

            migrationBuilder.DropTable(
                name: "brief_review_decisions");

            migrationBuilder.DropTable(
                name: "brief_schedule");

            migrationBuilder.DropTable(
                name: "collection_admission_batches");

            migrationBuilder.DropTable(
                name: "collection_candidate_jobs");

            migrationBuilder.DropTable(
                name: "collection_collector_slot");

            migrationBuilder.DropTable(
                name: "collection_job_origins");

            migrationBuilder.DropTable(
                name: "collection_source_schedules");

            migrationBuilder.DropTable(
                name: "document_change_analysis_budget");

            migrationBuilder.DropTable(
                name: "document_change_analysis_runs");

            migrationBuilder.DropTable(
                name: "document_change_analysis_status");

            migrationBuilder.DropTable(
                name: "document_change_review_decisions");

            migrationBuilder.DropTable(
                name: "document_change_review_idempotency");

            migrationBuilder.DropTable(
                name: "evidence_processing");

            migrationBuilder.DropTable(
                name: "executive_terms");

            migrationBuilder.DropTable(
                name: "publication_releases");

            migrationBuilder.DropTable(
                name: "reader_report_cursor");

            migrationBuilder.DropTable(
                name: "reader_reports");

            migrationBuilder.DropTable(
                name: "relationship_analysis_budget");

            migrationBuilder.DropTable(
                name: "relationship_review_decisions");

            migrationBuilder.DropTable(
                name: "roster_terms");

            migrationBuilder.DropTable(
                name: "statement_analysis_budget");

            migrationBuilder.DropTable(
                name: "statement_gate_labels");

            migrationBuilder.DropTable(
                name: "statement_review_decisions");

            migrationBuilder.DropTable(
                name: "vote_analysis_budget");

            migrationBuilder.DropTable(
                name: "vote_review_decisions");

            migrationBuilder.DropTable(
                name: "action_revisions");

            migrationBuilder.DropTable(
                name: "brief_revisions");

            migrationBuilder.DropTable(
                name: "collection_discoveries");

            migrationBuilder.DropTable(
                name: "collection_job_attempts");

            migrationBuilder.DropTable(
                name: "document_change_analysis");

            migrationBuilder.DropTable(
                name: "document_change_revisions");

            migrationBuilder.DropTable(
                name: "executive_officials");

            migrationBuilder.DropTable(
                name: "relationship_revisions");

            migrationBuilder.DropTable(
                name: "roster_members");

            migrationBuilder.DropTable(
                name: "statement_revisions");

            migrationBuilder.DropTable(
                name: "vote_revisions");

            migrationBuilder.DropTable(
                name: "action_analysis_runs");

            migrationBuilder.DropTable(
                name: "action_drafts");

            migrationBuilder.DropTable(
                name: "brief_analysis_runs");

            migrationBuilder.DropTable(
                name: "brief_drafts");

            migrationBuilder.DropTable(
                name: "document_change_drafts");

            migrationBuilder.DropTable(
                name: "relationship_analysis_runs");

            migrationBuilder.DropTable(
                name: "relationship_drafts");

            migrationBuilder.DropTable(
                name: "roster_snapshots");

            migrationBuilder.DropTable(
                name: "statement_analysis_runs");

            migrationBuilder.DropTable(
                name: "statement_drafts");

            migrationBuilder.DropTable(
                name: "vote_analysis_runs");

            migrationBuilder.DropTable(
                name: "vote_drafts");

            migrationBuilder.DropTable(
                name: "action_analysis");

            migrationBuilder.DropTable(
                name: "brief_analysis");

            migrationBuilder.DropTable(
                name: "document_comparisons");

            migrationBuilder.DropTable(
                name: "relationship_analysis");

            migrationBuilder.DropTable(
                name: "statement_analysis");

            migrationBuilder.DropTable(
                name: "vote_analysis");

            migrationBuilder.DropTable(
                name: "action_candidates");

            migrationBuilder.DropTable(
                name: "statement_candidates");

            migrationBuilder.DropTable(
                name: "vote_candidates");

            migrationBuilder.DropTable(
                name: "bill_gates");

            migrationBuilder.DropTable(
                name: "vote_versions");

            migrationBuilder.DropTable(
                name: "collection_jobs");

            migrationBuilder.DropTable(
                name: "document_extractions");

            migrationBuilder.DropTable(
                name: "collection_configuration_revisions");

            migrationBuilder.DropTable(
                name: "collection_attempts");

            migrationBuilder.DropTable(
                name: "collection_captures");

            migrationBuilder.Sql("""
                DROP FUNCTION civic_lens_notify_analysis_change();
                DROP FUNCTION civic_lens_notify_work_change();
                """);
        }
    }
}
