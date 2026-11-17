using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <summary>
    /// The baseline that EF generated from the model, then the hand SQL in <c>InitialSchema.HandWritten.cs</c>. Production keeps the check
    /// <c>ck_job_attempt_request_last_modified_utc_ticks</c> and has no copy of the tick range check that the model adds to the same column,
    /// so the generated <c>ck_collection_job_attempts_request_last_modified_utc_ticks</c> is left out.
    /// </summary>
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "person_registry_sequence");

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
                name: "analysis_output_schemas",
                columns: table => new
                {
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_output_schemas", x => x.text_sha256);
                    table.CheckConstraint("ck_analysis_output_schemas_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_analysis_output_schemas_hash", "text_sha256 = encode(sha256(convert_to(text, 'UTF8')), 'hex')");
                    table.CheckConstraint("ck_analysis_output_schemas_text", "char_length(text) BETWEEN 1 AND 100000");
                });

            migrationBuilder.CreateTable(
                name: "analysis_system_prompts",
                columns: table => new
                {
                    text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_system_prompts", x => x.text_sha256);
                    table.CheckConstraint("ck_analysis_system_prompts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_analysis_system_prompts_hash", "text_sha256 = encode(sha256(convert_to(text, 'UTF8')), 'hex')");
                    table.CheckConstraint("ck_analysis_system_prompts_text", "char_length(text) BETWEEN 1 AND 100000");
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
                    table.CheckConstraint("ck_bill_stream_cursors_updated_at_utc_ticks", "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.UniqueConstraint("ux_brief_drafts_record_week", x => new { x.record_id, x.week_start });
                    table.CheckConstraint("ck_brief_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_brief_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_brief_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
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
                    table.CheckConstraint("ck_document_change_analysis_status_paused_until_utc_ticks", "paused_until_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_analysis_status_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    held_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_drafts", x => x.record_id);
                    table.CheckConstraint("ck_executive_action_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_executive_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_executive_action_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_executive_action_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_executive_action_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
                    table.CheckConstraint("ck_executive_action_drafts_revision", "current_revision_number >= 1");
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
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    code_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_releases", x => x.release_number);
                    table.CheckConstraint("ck_publication_releases_code_version", "code_version ~ '^[0-9a-f]{40}$'");
                    table.CheckConstraint("ck_publication_releases_directory", "directory_name ~ '^[0-9]{6}-[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_publication_releases_number", "release_number BETWEEN 1 AND 999999");
                    table.CheckConstraint("ck_publication_releases_published_at_utc_ticks", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                });

            migrationBuilder.CreateTable(
                name: "published_correction_histories",
                columns: table => new
                {
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encoding_version = table.Column<int>(type: "integer", nullable: false),
                    correction_count = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_correction_histories", x => x.history_id);
                    table.CheckConstraint("ck_published_correction_histories_correction_count", "correction_count BETWEEN 0 AND 62");
                    table.CheckConstraint("ck_published_correction_histories_encoding_version", "encoding_version = 1");
                    table.CheckConstraint("ck_published_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "published_records",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_records", x => new { x.kind, x.record_id });
                    table.CheckConstraint("ck_published_records_first_published_at_utc_ticks", "first_published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_records_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_published_records_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
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
                    table.CheckConstraint("ck_reader_reports_received_at_utc_ticks", "received_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_reader_reports_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
                    table.CheckConstraint("ck_reader_reports_resolution", "(status = 'Open') = (resolved_by IS NULL AND resolved_at_utc_ticks IS NULL) AND (status <> 'CorrectionNeeded' OR note IS NOT NULL)");
                    table.CheckConstraint("ck_reader_reports_resolved_at_utc_ticks", "resolved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.CheckConstraint("ck_relationship_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_relationship_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_relationship_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
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
                name: "vote_member_lists",
                columns: table => new
                {
                    list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    member_count = table.Column<short>(type: "smallint", nullable: false),
                    yea_count = table.Column<short>(type: "smallint", nullable: false),
                    nay_count = table.Column<short>(type: "smallint", nullable: false),
                    present_count = table.Column<short>(type: "smallint", nullable: false),
                    not_voting_count = table.Column<short>(type: "smallint", nullable: false),
                    other_count = table.Column<short>(type: "smallint", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_member_lists", x => x.list_id);
                    table.CheckConstraint("ck_vote_member_lists_count", "member_count BETWEEN 1 AND 600");
                    table.CheckConstraint("ck_vote_member_lists_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_member_lists_id", "list_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_member_lists_totals", "yea_count >= 0 AND nay_count >= 0 AND present_count >= 0 AND not_voting_count >= 0 AND other_count >= 0 AND (yea_count::integer + nay_count + present_count + not_voting_count + other_count) = member_count");
                });

            migrationBuilder.CreateTable(
                name: "worker_heartbeat",
                columns: table => new
                {
                    loop = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    beat_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_heartbeat", x => x.loop);
                    table.CheckConstraint("ck_worker_heartbeat_beat_at_utc_ticks", "beat_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    elapsed_milliseconds = table.Column<long>(type: "bigint", nullable: true),
                    request_count = table.Column<int>(type: "integer", nullable: true),
                    bytes_received = table.Column<long>(type: "bigint", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    retry_delay_ticks = table.Column<long>(type: "bigint", nullable: true),
                    capture_sha256 = table.Column<string>(type: "text", nullable: true),
                    prior_capture_attempt_id = table.Column<string>(type: "text", nullable: true),
                    prior_capture_sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_collection_attempts_elapsed_milliseconds", "elapsed_milliseconds IS NULL OR elapsed_milliseconds BETWEEN 0 AND 305000");
                    table.CheckConstraint("ck_collection_attempts_identity", "length(btrim(attempt_id)) > 0 AND length(btrim(source_id)) > 0 AND length(btrim(requested_url)) > 0 AND length(btrim(final_url)) > 0");
                    table.CheckConstraint("ck_collection_attempts_observed_at_utc_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_outcome", "(outcome = 'captured' AND has_response AND response_status_code IS NOT NULL AND response_status_code = 200 AND capture_sha256 IS NOT NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'notModified' AND has_response AND has_sent_validators AND response_status_code IS NOT NULL AND response_status_code = 304 AND capture_sha256 IS NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL)\nOR (outcome = 'failed' AND failure_code IS NOT NULL AND retry_delay_ticks IS NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)\nOR (outcome = 'deferred' AND failure_code IS NOT NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_prior_outcome", "prior_capture_attempt_id IS NULL OR (outcome = 'notModified' AND prior_capture_attempt_id <> attempt_id)");
                    table.CheckConstraint("ck_collection_attempts_prior_pair", "(prior_capture_attempt_id IS NULL) = (prior_capture_sha256 IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response", "(has_response AND response_status_code IS NOT NULL AND response_status_code BETWEEN 100 AND 599 AND response_content_encodings IS NOT NULL AND array_position(response_content_encodings, NULL) IS NULL)\nOR (NOT has_response AND response_status_code IS NULL AND response_etag IS NULL AND response_last_modified_utc_ticks IS NULL AND response_content_type IS NULL AND response_content_encodings IS NULL)");
                    table.CheckConstraint("ck_collection_attempts_response_last_modified_utc_ticks", "response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
                    table.CheckConstraint("ck_collection_attempts_robots_delay", "robots_crawl_delay_milliseconds IS NULL OR robots_crawl_delay_milliseconds BETWEEN 0 AND 922337203685000");
                    table.CheckConstraint("ck_collection_attempts_sent_last_modified_utc_ticks", "sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_attempts_transfer", "(request_count IS NULL) = (bytes_received IS NULL) AND (request_count IS NULL OR request_count >= 0 AND bytes_received >= 0 AND (bytes_received = 0 OR request_count > 0))");
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
                    priority = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)3),
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
                    table.CheckConstraint("ck_job_priority", "priority BETWEEN 1 AND 3");
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
                    table.CheckConstraint("ck_collection_source_schedules_interval", "interval_seconds BETWEEN 60 AND 604800");
                    table.CheckConstraint("ck_collection_source_schedules_next_due_utc_ticks", "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_collection_source_schedules_revision", "configuration_revision_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "fk_collection_source_schedules_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_record_states",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    last_release = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_record_states", x => new { x.kind, x.record_id, x.first_release });
                    table.CheckConstraint("ck_publication_record_states_range", "last_release IS NULL OR last_release >= first_release");
                    table.ForeignKey(
                        name: "FK_publication_record_states_publication_releases_first_release",
                        column: x => x.first_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_record_states_publication_releases_last_release",
                        column: x => x.last_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_bill_counts",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bills = table.Column<int>(type: "integer", nullable: false),
                    expected_member_row_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_bill_counts", x => new { x.release_number, x.congress });
                    table.CheckConstraint("ck_release_bill_counts_bills", "bills >= 0");
                    table.CheckConstraint("ck_release_bill_counts_congress", "congress > 0");
                    table.CheckConstraint("ck_release_bill_counts_expected_member_row_count", "expected_member_row_count >= 0");
                    table.ForeignKey(
                        name: "FK_release_bill_counts_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_bill_statuses",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    latest_action_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    latest_action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    read_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_bill_statuses", x => new { x.release_number, x.record_id });
                    table.CheckConstraint("ck_release_bill_statuses_latest_action_text", "length(btrim(latest_action_text)) > 0 AND length(latest_action_text) <= 1000");
                    table.CheckConstraint("ck_release_bill_statuses_read_at_utc_ticks", "read_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_release_bill_statuses_record_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_release_bill_statuses_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issues",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    definition = table.Column<string>(type: "text", nullable: true),
                    count_congress = table.Column<int>(type: "integer", nullable: true),
                    substantive_votes = table.Column<long>(type: "bigint", nullable: true),
                    bills = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issues", x => new { x.release_number, x.issue_id });
                    table.CheckConstraint("ck_release_issues_home_counts", "(count_congress IS NULL AND substantive_votes IS NULL AND bills IS NULL) OR (count_congress IS NOT NULL AND substantive_votes IS NOT NULL AND bills IS NOT NULL AND count_congress > 0 AND substantive_votes >= 0 AND bills >= 0)");
                    table.CheckConstraint("ck_release_issues_issue_id", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_release_issues_name", "length(btrim(name)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issues_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_officials",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sort_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    wikidata_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    photo_path = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_officials", x => new { x.release_number, x.official_id });
                    table.CheckConstraint("ck_release_officials_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_release_officials_official_id", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_release_officials_photo_path", "photo_path IS NULL OR length(btrim(photo_path)) > 0");
                    table.CheckConstraint("ck_release_officials_sort_name", "sort_name IS NULL OR length(btrim(sort_name)) > 0");
                    table.ForeignKey(
                        name: "FK_release_officials_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_policy_areas",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_policy_areas", x => new { x.release_number, x.policy_area_id });
                    table.CheckConstraint("ck_release_policy_areas_name", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_release_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
                    table.ForeignKey(
                        name: "FK_release_policy_areas_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_vote_bills",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    vote_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bill_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_vote_bills", x => new { x.release_number, x.vote_record_id });
                    table.CheckConstraint("ck_release_vote_bills_bill_record_id", "bill_record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_release_vote_bills_vote_record_id", "vote_record_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_release_vote_bills_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_correction_history_entries",
                columns: table => new
                {
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    replaced_revision_number = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    published_offset_minutes = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    text_rewrite = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_correction_history_entries", x => new { x.history_id, x.position });
                    table.CheckConstraint("ck_published_correction_history_entries_local_ticks", "published_at_utc_ticks + published_offset_minutes::bigint * 600000000 BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_correction_history_entries_note", "note IS NULL OR length(note) <= 500");
                    table.CheckConstraint("ck_published_correction_history_entries_note_text_rewrite", "note IS NULL OR text_rewrite = 'None'");
                    table.CheckConstraint("ck_published_correction_history_entries_position", "position BETWEEN 0 AND 61");
                    table.CheckConstraint("ck_published_correction_history_entries_published_at_utc_ticks", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_correction_history_entries_published_offset_minut~", "published_offset_minutes BETWEEN -840 AND 840");
                    table.CheckConstraint("ck_published_correction_history_entries_replaced_revision_numb~", "replaced_revision_number >= 1");
                    table.CheckConstraint("ck_published_correction_history_entries_text_rewrite", "text_rewrite IN ('None', 'Model', 'Code')");
                    table.ForeignKey(
                        name: "FK_published_correction_history_entries_published_correction_h~",
                        column: x => x.history_id,
                        principalTable: "published_correction_histories",
                        principalColumn: "history_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "publication_withdrawals",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_revision_number = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    last_release = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_withdrawals", x => new { x.kind, x.record_id, x.first_release });
                    table.CheckConstraint("ck_publication_withdrawals_note", "char_length(note) BETWEEN 1 AND 500 AND btrim(note) <> ''");
                    table.CheckConstraint("ck_publication_withdrawals_range", "last_release IS NULL OR last_release >= first_release");
                    table.CheckConstraint("ck_publication_withdrawals_revision", "withdrawn_revision_number >= 1");
                    table.CheckConstraint("ck_publication_withdrawals_withdrawn_at_utc_ticks", "withdrawn_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_publication_releases_first_release",
                        column: x => x.first_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_publication_releases_last_release",
                        column: x => x.last_release,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_publication_withdrawals_published_records_kind_record_id",
                        columns: x => new { x.kind, x.record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
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
                    profile_json = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    part = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_extractions", x => x.extraction_id);
                    table.CheckConstraint("ck_document_extractions_hashes", "extraction_id ~ '^[0-9a-f]{64}$' AND text_sha256 ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_document_extractions_part", "part IS NULL OR part >= 0");
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
                name: "official_photos",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    credit = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_official_photos", x => x.attempt_id);
                    table.ForeignKey(
                        name: "FK_official_photos_collection_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "collection_attempts",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    wikidata_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    govtrack_id = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    created_by_attempt_id = table.Column<string>(type: "text", nullable: true),
                    created_by_actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.person_id);
                    table.CheckConstraint("ck_people_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_people_creator", "num_nonnulls(created_by_attempt_id, created_by_actor) = 1");
                    table.CheckConstraint("ck_people_identity", "person_id ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND (bioguide_id IS NULL OR bioguide_id ~ '^[A-Z][0-9]{6}$') AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND (govtrack_id IS NULL OR govtrack_id > 0)");
                    table.ForeignKey(
                        name: "FK_people_collection_attempts_created_by_attempt_id",
                        column: x => x.created_by_attempt_id,
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
                    table.CheckConstraint("ck_bill_stream_pages_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    request_version = table.Column<int>(type: "integer", nullable: false),
                    request_job_id = table.Column<string>(type: "text", nullable: false),
                    request_source_id = table.Column<string>(type: "text", nullable: false),
                    request_url = table.Column<string>(type: "text", nullable: false),
                    request_allowed_origin = table.Column<string>(type: "text", nullable: false),
                    request_allowed_path_prefix = table.Column<string>(type: "text", nullable: false),
                    request_artifact_directory = table.Column<string>(type: "text", nullable: false),
                    request_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    request_max_candidates = table.Column<int>(type: "integer", nullable: false),
                    request_max_requests = table.Column<int>(type: "integer", nullable: false),
                    request_max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    request_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    request_min_delay_milliseconds = table.Column<int>(type: "integer", nullable: false),
                    request_etag = table.Column<string>(type: "text", nullable: true),
                    request_last_modified_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    request_credential = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    retry_delay_ticks = table.Column<long>(type: "bigint", nullable: true),
                    robots_crawl_delay_milliseconds = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_job_attempts", x => x.attempt_id);
                    table.CheckConstraint("ck_job_attempt_outcome", "outcome IS NULL OR outcome IN ('Succeeded','RetryableFailure','PermanentFailure','Interrupted')");
                    table.CheckConstraint("ck_job_attempt_request_budgets", "request_max_candidates BETWEEN 1 AND 1000 AND request_max_requests BETWEEN 2 AND 20 AND request_max_bytes BETWEEN 1 AND 100000000 AND request_timeout_seconds BETWEEN 1 AND 300 AND request_min_delay_milliseconds BETWEEN 0 AND 60000");
                    table.CheckConstraint("ck_job_attempt_request_credential", "request_credential IS NULL OR request_mode = 'Api'");
                    table.CheckConstraint("ck_job_attempt_request_identity", "request_version = 1 AND length(btrim(request_job_id)) > 0 AND length(btrim(request_source_id)) > 0 AND length(btrim(request_url)) > 0 AND length(btrim(request_allowed_origin)) > 0 AND length(btrim(request_allowed_path_prefix)) > 0 AND length(btrim(request_artifact_directory)) > 0");
                    table.CheckConstraint("ck_job_attempt_request_last_modified_utc_ticks", "request_last_modified_utc_ticks IS NULL OR request_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_job_attempt_request_mode", "request_mode IN ('Page','Feed','Html','Api')");
                    table.CheckConstraint("ck_job_attempt_resolution", "(outcome IS NULL) = (completed_at IS NULL) AND (completed_at IS NOT NULL OR error_code IS NULL AND retry_delay_ticks IS NULL AND robots_crawl_delay_milliseconds IS NULL)");
                    table.CheckConstraint("ck_job_attempt_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
                    table.CheckConstraint("ck_job_attempt_robots_delay", "robots_crawl_delay_milliseconds IS NULL OR (robots_crawl_delay_milliseconds BETWEEN 0 AND 922337203685000 AND outcome <> 'Interrupted')");
                    table.CheckConstraint("ck_job_attempt_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "FK_collection_job_attempts_collection_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issue_excludes",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_excludes", x => new { x.release_number, x.issue_id, x.position });
                    table.UniqueConstraint("AK_release_issue_excludes_release_number_issue_id_text", x => new { x.release_number, x.issue_id, x.text });
                    table.CheckConstraint("ck_release_issue_excludes_position", "position >= 0");
                    table.CheckConstraint("ck_release_issue_excludes_text", "length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issue_excludes_release_issues_release_number_issue_~",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issues",
                        principalColumns: new[] { "release_number", "issue_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issue_includes",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_includes", x => new { x.release_number, x.issue_id, x.position });
                    table.UniqueConstraint("AK_release_issue_includes_release_number_issue_id_text", x => new { x.release_number, x.issue_id, x.text });
                    table.CheckConstraint("ck_release_issue_includes_position", "position >= 0");
                    table.CheckConstraint("ck_release_issue_includes_text", "length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "FK_release_issue_includes_release_issues_release_number_issue_~",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issues",
                        principalColumns: new[] { "release_number", "issue_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_issue_party_lineups",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    unknown_bills = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_party_lineups", x => new { x.release_number, x.issue_id });
                    table.CheckConstraint("ck_release_issue_party_lineups_unknown_bills", "unknown_bills >= 0");
                    table.ForeignKey(
                        name: "fk_release_issue_party_lineups_issue",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issues",
                        principalColumns: new[] { "release_number", "issue_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_official_bill_counts",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    sponsored = table.Column<int>(type: "integer", nullable: false),
                    cosponsored = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_official_bill_counts", x => new { x.release_number, x.congress, x.official_id });
                    table.CheckConstraint("ck_release_official_bill_counts_congress", "congress > 0");
                    table.CheckConstraint("ck_release_official_bill_counts_cosponsored", "cosponsored >= 0");
                    table.CheckConstraint("ck_release_official_bill_counts_nonzero", "sponsored + cosponsored > 0");
                    table.CheckConstraint("ck_release_official_bill_counts_official_id", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_release_official_bill_counts_sponsored", "sponsored >= 0");
                    table.ForeignKey(
                        name: "fk_release_official_bill_counts_header",
                        columns: x => new { x.release_number, x.congress },
                        principalTable: "release_bill_counts",
                        principalColumns: new[] { "release_number", "congress" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_release_official_bill_counts_official",
                        columns: x => new { x.release_number, x.official_id },
                        principalTable: "release_officials",
                        principalColumns: new[] { "release_number", "official_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_official_record_counts",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    records = table.Column<int>(type: "integer", nullable: false),
                    votes = table.Column<int>(type: "integer", nullable: false),
                    bills = table.Column<int>(type: "integer", nullable: false),
                    statements = table.Column<int>(type: "integer", nullable: false),
                    executive_actions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_official_record_counts", x => new { x.release_number, x.official_id });
                    table.CheckConstraint("ck_release_official_record_counts_counts", "records >= 0 AND votes >= 0 AND bills >= 0 AND statements >= 0 AND executive_actions >= 0");
                    table.CheckConstraint("ck_release_official_record_counts_official_id", "length(btrim(official_id)) > 0");
                    table.ForeignKey(
                        name: "fk_release_official_record_counts_official",
                        columns: x => new { x.release_number, x.official_id },
                        principalTable: "release_officials",
                        principalColumns: new[] { "release_number", "official_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_official_terms",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    office = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    district = table.Column<int>(type: "integer", nullable: true),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    caucus = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    start_on = table.Column<DateOnly>(type: "date", nullable: false),
                    end_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_official_terms", x => new { x.release_number, x.official_id, x.position });
                    table.CheckConstraint("ck_release_official_terms_dates", "end_on IS NULL OR end_on >= start_on");
                    table.CheckConstraint("ck_release_official_terms_district", "district IS NULL OR district >= 0");
                    table.CheckConstraint("ck_release_official_terms_kind", "kind IN ('Senator', 'Representative', 'Executive', 'Judicial', 'Other')");
                    table.CheckConstraint("ck_release_official_terms_office", "length(btrim(office)) > 0");
                    table.CheckConstraint("ck_release_official_terms_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_release_official_terms_release_officials_release_number_off~",
                        columns: x => new { x.release_number, x.official_id },
                        principalTable: "release_officials",
                        principalColumns: new[] { "release_number", "official_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "publication_withdrawal_corrections",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    first_release = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    replaced_revision_number = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    text_rewrite = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_withdrawal_corrections", x => new { x.kind, x.record_id, x.first_release, x.position });
                    table.CheckConstraint("ck_publication_withdrawal_corrections_note", "note IS NULL OR char_length(note) BETWEEN 1 AND 500 AND btrim(note) <> ''");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_note_text_rewrite", "note IS NULL OR text_rewrite = 'None'");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_position", "\"position\" >= 0");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_published_at_utc_ticks", "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_revision", "replaced_revision_number >= 1");
                    table.CheckConstraint("ck_publication_withdrawal_corrections_text_rewrite", "text_rewrite IN ('None', 'Model', 'Code')");
                    table.ForeignKey(
                        name: "FK_publication_withdrawal_corrections_publication_withdrawals_~",
                        columns: x => new { x.kind, x.record_id, x.first_release },
                        principalTable: "publication_withdrawals",
                        principalColumns: new[] { "kind", "record_id", "first_release" },
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
                name: "bill_text_versions",
                columns: table => new
                {
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bill_number = table.Column<int>(type: "integer", nullable: false),
                    package_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    version_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chars_total = table.Column<int>(type: "integer", nullable: false),
                    chars_read = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_text_versions", x => new { x.congress, x.bill_type, x.bill_number, x.package_id });
                    table.CheckConstraint("ck_bill_text_versions_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_bill_text_versions_chars", "chars_read >= 1 AND chars_read <= 60000 AND chars_read <= chars_total AND (chars_read = chars_total OR chars_total > 60000)");
                    table.CheckConstraint("ck_bill_text_versions_package", "package_id ~ ('^BILLS-' || congress || bill_type || bill_number || '[a-z]+$') OR package_id ~ ('^PLAW-' || congress || '(publ|pvtl)[0-9]+$')");
                    table.CheckConstraint("ck_bill_text_versions_type", "version_type = btrim(version_type) AND char_length(version_type) >= 1");
                    table.CheckConstraint("ck_bill_text_versions_url", "source_url LIKE 'https://www.congress.gov/%'");
                    table.ForeignKey(
                        name: "FK_bill_text_versions_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
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
                    has_changes = table.Column<bool>(type: "boolean", nullable: false),
                    hunk_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_comparisons", x => x.comparison_id);
                    table.CheckConstraint("ck_document_comparisons_hunk_count", "hunk_count IS NULL OR (hunk_count >= 0 AND has_changes = (hunk_count > 0))");
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
                    table.UniqueConstraint("ux_statement_candidates_candidate_extraction", x => new { x.candidate_id, x.extraction_id });
                    table.CheckConstraint("ck_statement_candidates_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                name: "vote_member_list_entries",
                columns: table => new
                {
                    list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    person_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vote_cast = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_member_list_entries", x => new { x.list_id, x.position });
                    table.CheckConstraint("ck_vote_member_list_entries_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_vote_member_list_entries_member", "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0 AND length(btrim(vote_cast)) > 0");
                    table.CheckConstraint("ck_vote_member_list_entries_party", "party IS NULL OR length(btrim(party)) > 0");
                    table.CheckConstraint("ck_vote_member_list_entries_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_vote_member_list_entries_document_extractions_citation_extr~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_member_list_entries_vote_member_lists_list_id",
                        column: x => x.list_id,
                        principalTable: "vote_member_lists",
                        principalColumn: "list_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_versions",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_call_number = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    update_date_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bill_number = table.Column<int>(type: "integer", nullable: true),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_versions", x => new { x.detail_job_id, x.session_number, x.roll_call_number });
                    table.CheckConstraint("ck_vote_versions_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
                    table.CheckConstraint("ck_vote_versions_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_versions_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.CheckConstraint("ck_vote_versions_update_date_utc_ticks", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                name: "person_office_revisions",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    registry_sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('person_registry_sequence')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_office_revisions", x => new { x.person_id, x.revision_number });
                    table.CheckConstraint("ck_person_office_revisions_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_person_office_revisions_shape", "revision_number >= 1 AND length(btrim(note)) > 0 AND length(btrim(actor_subject)) > 0");
                    table.ForeignKey(
                        name: "FK_person_office_revisions_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "person_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_setting_revisions",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    withhold_photo = table.Column<bool>(type: "boolean", nullable: false),
                    website = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recorded_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    registry_sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('person_registry_sequence')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_setting_revisions", x => new { x.person_id, x.revision_number });
                    table.CheckConstraint("ck_person_setting_revisions_recorded_at_utc_ticks", "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_person_setting_revisions_shape", "revision_number >= 1 AND (website IS NULL OR length(btrim(website)) > 0) AND length(btrim(note)) > 0 AND length(btrim(actor_subject)) > 0");
                    table.ForeignKey(
                        name: "FK_person_setting_revisions_people_person_id",
                        column: x => x.person_id,
                        principalTable: "people",
                        principalColumn: "person_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_officials",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    govtrack_id = table.Column<int>(type: "integer", nullable: false),
                    wikidata_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    last_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_officials", x => new { x.attempt_id, x.govtrack_id });
                    table.CheckConstraint("ck_executive_officials_identity", "govtrack_id > 0 AND (wikidata_id IS NULL OR wikidata_id ~ '^Q[1-9][0-9]*$') AND length(btrim(full_name)) > 0");
                    table.CheckConstraint("ck_executive_officials_last_name", "last_name IS NULL OR length(btrim(last_name)) > 0");
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
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    last_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    office = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roster_members", x => new { x.attempt_id, x.bioguide_id });
                    table.CheckConstraint("ck_roster_members_identity", "bioguide_id ~ '^[A-Z][0-9]{6}$' AND length(btrim(full_name)) > 0");
                    table.CheckConstraint("ck_roster_members_last_name", "last_name IS NULL OR length(btrim(last_name)) > 0");
                    table.ForeignKey(
                        name: "FK_roster_members_roster_snapshots_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "roster_snapshots",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zcta_districts",
                columns: table => new
                {
                    attempt_id = table.Column<string>(type: "text", nullable: false),
                    zcta = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    state = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    district = table.Column<int>(type: "integer", nullable: false),
                    land_area_square_meters = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zcta_districts", x => new { x.attempt_id, x.zcta, x.state, x.district });
                    table.CheckConstraint("ck_zcta_districts_land", "district >= 0 AND land_area_square_meters > 0");
                    table.CheckConstraint("ck_zcta_districts_zcta", "zcta ~ '^[0-9]{5}$'");
                    table.ForeignKey(
                        name: "FK_zcta_districts_roster_snapshots_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "roster_snapshots",
                        principalColumn: "attempt_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collection_collector_slot",
                columns: table => new
                {
                    pacing_key = table.Column<string>(type: "text", nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    attempt_id = table.Column<string>(type: "character varying(128)", nullable: true),
                    token = table.Column<string>(type: "text", nullable: true),
                    fence = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_collector_slot", x => x.pacing_key);
                    table.CheckConstraint("ck_collector_lease", "(token IS NULL AND expires_at IS NULL AND job_id IS NULL AND attempt_id IS NULL) OR (token IS NOT NULL AND expires_at IS NOT NULL AND job_id IS NOT NULL AND attempt_id IS NOT NULL)");
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
                name: "release_issue_party_counts",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    bills = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_issue_party_counts", x => new { x.release_number, x.issue_id, x.party });
                    table.CheckConstraint("ck_release_issue_party_counts_bills", "bills > 0");
                    table.CheckConstraint("ck_release_issue_party_counts_party", "length(btrim(party)) > 0");
                    table.ForeignKey(
                        name: "fk_release_issue_party_counts_lineup",
                        columns: x => new { x.release_number, x.issue_id },
                        principalTable: "release_issue_party_lineups",
                        principalColumns: new[] { "release_number", "issue_id" },
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
                    subject_count = table.Column<int>(type: "integer", nullable: true),
                    text_state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    text_package_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    latest_action_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    latest_action_date = table.Column<DateOnly>(type: "date", nullable: true),
                    latest_action_text = table.Column<string>(type: "text", nullable: true),
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
                    table.CheckConstraint("ck_bill_gates_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_bill_gates_decided", "(decision = 'Pending') = (decided_at_utc_ticks IS NULL)");
                    table.CheckConstraint("ck_bill_gates_decided_at_utc_ticks", "decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_bill_gates_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_bill_gates_latest_action", "(latest_action_date IS NULL) = (latest_action_text IS NULL) AND (latest_action_text IS NULL OR latest_action_read AND char_length(latest_action_text) BETWEEN 1 AND 1000 AND latest_action_text = btrim(latest_action_text))");
                    table.CheckConstraint("ck_bill_gates_reason", "(decision = 'Pending' AND reason IS NULL) OR (decision = 'Passed' AND reason IS NOT NULL AND reason IN ('CoveredSponsor','TrackedPolicyArea','CoveredCosponsor')) OR (decision = 'NotRelevant' AND reason IS NOT NULL AND reason IN ('NoPolicyArea','UntrackedPolicyArea'))");
                    table.CheckConstraint("ck_bill_gates_subject_count", "subject_count >= 0");
                    table.CheckConstraint("ck_bill_gates_summary_count", "summary_count >= 0");
                    table.CheckConstraint("ck_bill_gates_text_package", "COALESCE(text_state = 'Read', false) = (text_package_id IS NOT NULL)");
                    table.CheckConstraint("ck_bill_gates_text_state", "text_state IN ('NoText','Read')");
                    table.CheckConstraint("ck_bill_gates_text_without_summary", "text_state IS NULL OR summary_count = 0");
                    table.CheckConstraint("ck_bill_gates_update_date_utc_ticks", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_bill_gates_bill_text_versions_congress_bill_type_bill_numbe~",
                        columns: x => new { x.congress, x.bill_type, x.bill_number, x.text_package_id },
                        principalTable: "bill_text_versions",
                        principalColumns: new[] { "congress", "bill_type", "bill_number", "package_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_gates_collection_jobs_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_drafts", x => x.draft_id);
                    table.UniqueConstraint("ux_document_change_drafts_draft_comparison", x => new { x.draft_id, x.comparison_id });
                    table.CheckConstraint("ck_document_change_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_drafts_id", "draft_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
                    table.CheckConstraint("ck_document_change_drafts_versions", "current_revision_number >= 1 AND review_state_version >= 0");
                    table.ForeignKey(
                        name: "FK_document_change_drafts_document_comparisons_comparison_id",
                        column: x => x.comparison_id,
                        principalTable: "document_comparisons",
                        principalColumn: "comparison_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_comparison_hunks",
                columns: table => new
                {
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    before_start = table.Column<int>(type: "integer", nullable: false),
                    before_length = table.Column<int>(type: "integer", nullable: false),
                    after_start = table.Column<int>(type: "integer", nullable: false),
                    after_length = table.Column<int>(type: "integer", nullable: false),
                    before_context_start = table.Column<int>(type: "integer", nullable: false),
                    before_context_length = table.Column<int>(type: "integer", nullable: false),
                    after_context_start = table.Column<int>(type: "integer", nullable: false),
                    after_context_length = table.Column<int>(type: "integer", nullable: false),
                    word_edit_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_comparison_hunks", x => new { x.comparison_id, x.position });
                    table.CheckConstraint("ck_document_comparison_hunks_position", "position >= 0");
                    table.CheckConstraint("ck_document_comparison_hunks_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0 AND before_context_start >= 0 AND before_context_length >= 0 AND after_context_start >= 0 AND after_context_length >= 0");
                    table.CheckConstraint("ck_document_comparison_hunks_word_edit_count", "word_edit_count >= 0");
                    table.ForeignKey(
                        name: "FK_document_comparison_hunks_document_comparisons_comparison_id",
                        column: x => x.comparison_id,
                        principalTable: "document_comparisons",
                        principalColumn: "comparison_id",
                        onDelete: ReferentialAction.Cascade);
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
                    table.CheckConstraint("ck_evidence_processing_observed_at_utc_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_evidence_processing_stage", "stage IN ('Preparation','Extraction','Comparison','Complete')");
                    table.CheckConstraint("ck_evidence_processing_status", "status IN ('Pending','Running','RetryWaiting','WaitingForPredecessor','Succeeded','Blocked','Failed')");
                    table.ForeignKey(
                        name: "FK_evidence_processing_document_comparisons_comparison_id",
                        column: x => x.comparison_id,
                        principalTable: "document_comparisons",
                        principalColumn: "comparison_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_processing_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
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
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    held_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_drafts", x => x.candidate_id);
                    table.CheckConstraint("ck_statement_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_drafts_not_drafted", "(current_revision_number = 0) = (review_status = 'NotDrafted')");
                    table.CheckConstraint("ck_statement_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_statement_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','NotDrafted','Rejected')");
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
                    roster_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
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
                    table.CheckConstraint("ck_vote_candidates_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_candidates_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.CheckConstraint("ck_vote_candidates_update_date_utc_ticks", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_vote_candidates_collection_jobs_members_job_id",
                        column: x => x.members_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_candidates_collection_jobs_roster_job_id",
                        column: x => x.roster_job_id,
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
                        name: "FK_vote_candidates_vote_versions_detail_job_id_session_number_~",
                        columns: x => new { x.detail_job_id, x.session_number, x.roll_call_number },
                        principalTable: "vote_versions",
                        principalColumns: new[] { "detail_job_id", "session_number", "roll_call_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vote_candidates_configuration_revision",
                        column: x => x.configuration_revision_id,
                        principalTable: "collection_configuration_revisions",
                        principalColumn: "revision_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "person_office_revision_terms",
                columns: table => new
                {
                    person_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_person_office_revision_terms", x => new { x.person_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_person_office_revision_terms_kind", "kind IN ('Executive', 'Judicial')");
                    table.CheckConstraint("ck_person_office_revision_terms_shape", "position >= 0 AND length(btrim(title)) > 0 AND (party IS NULL OR length(btrim(party)) > 0) AND (ends_on IS NULL OR ends_on >= starts_on)");
                    table.ForeignKey(
                        name: "FK_person_office_revision_terms_person_office_revisions_person~",
                        columns: x => new { x.person_id, x.revision_number },
                        principalTable: "person_office_revisions",
                        principalColumns: new[] { "person_id", "revision_number" },
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
                    party = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    caucus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
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
                    includes_summaries = table.Column<bool>(type: "boolean", nullable: false),
                    includes_subjects = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_candidates", x => x.candidate_id);
                    table.CheckConstraint("ck_action_candidates_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_action_candidates_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_candidates_update_date_utc_ticks", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    closes_chain = table.Column<bool>(type: "boolean", nullable: false),
                    imported_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_pages", x => x.job_id);
                    table.CheckConstraint("ck_bill_pages_detail", "role <> 'Detail' OR (job_id = detail_job_id AND page_order = 0 AND closes_chain)");
                    table.CheckConstraint("ck_bill_pages_order", "page_order >= 0");
                    table.CheckConstraint("ck_bill_pages_role", "role IN ('Detail','Cosponsors','Actions','Summaries','Subjects')");
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
                name: "published_bill_histories",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bill_number = table.Column<int>(type: "integer", nullable: false),
                    observed_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    action_count = table.Column<int>(type: "integer", nullable: false),
                    public_law = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_bill_histories", x => new { x.detail_job_id, x.projection_version });
                    table.CheckConstraint("ck_published_bill_histories_action_count", "action_count >= 0");
                    table.CheckConstraint("ck_published_bill_histories_bill_number", "bill_number >= 1");
                    table.CheckConstraint("ck_published_bill_histories_congress", "congress >= 1");
                    table.CheckConstraint("ck_published_bill_histories_observed_at_utc_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_bill_histories_projection_version", "projection_version >= 0");
                    table.ForeignKey(
                        name: "FK_published_bill_histories_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revisions",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    significance = table.Column<string>(type: "text", nullable: true),
                    limits = table.Column<string>(type: "text", nullable: true),
                    institution = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    change_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revisions", x => new { x.draft_id, x.revision_number });
                    table.CheckConstraint("ck_document_change_revisions_author_kind", "author_kind IN ('Person', 'Analysis', 'Code')");
                    table.CheckConstraint("ck_document_change_revisions_columns_written", "num_nulls(comparison_id, headline, summary, institution) IN (0, 4)");
                    table.CheckConstraint("ck_document_change_revisions_comparison", "comparison_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_document_change_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_revisions_limits", "length(limits) <= 4000");
                    table.CheckConstraint("ck_document_change_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_document_change_revisions_significance", "length(significance) <= 4000");
                    table.CheckConstraint("ck_document_change_revisions_summary", "length(summary) <= 8000");
                    table.ForeignKey(
                        name: "FK_document_change_revisions_document_change_drafts_draft_id",
                        column: x => x.draft_id,
                        principalTable: "document_change_drafts",
                        principalColumn: "draft_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_revisions_document_change_drafts_draft_id_c~",
                        columns: x => new { x.draft_id, x.comparison_id },
                        principalTable: "document_change_drafts",
                        principalColumns: new[] { "draft_id", "comparison_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_comparison_word_edits",
                columns: table => new
                {
                    comparison_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hunk_position = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    before_start = table.Column<int>(type: "integer", nullable: false),
                    before_length = table.Column<int>(type: "integer", nullable: false),
                    after_start = table.Column<int>(type: "integer", nullable: false),
                    after_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_comparison_word_edits", x => new { x.comparison_id, x.hunk_position, x.position });
                    table.CheckConstraint("ck_document_comparison_word_edits_position", "hunk_position >= 0 AND position >= 0");
                    table.CheckConstraint("ck_document_comparison_word_edits_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0");
                    table.ForeignKey(
                        name: "FK_document_comparison_word_edits_document_comparison_hunks_co~",
                        columns: x => new { x.comparison_id, x.hunk_position },
                        principalTable: "document_comparison_hunks",
                        principalColumns: new[] { "comparison_id", "position" },
                        onDelete: ReferentialAction.Cascade);
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
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    held_reason = table.Column<string>(type: "text", nullable: true),
                    refused_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_drafts", x => x.record_id);
                    table.UniqueConstraint("ux_vote_drafts_record_roll", x => new { x.record_id, x.chamber, x.congress, x.session_number, x.roll_call_number });
                    table.CheckConstraint("ck_vote_drafts_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_vote_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
                    table.CheckConstraint("ck_vote_drafts_revision", "current_revision_number >= 1");
                    table.CheckConstraint("ck_vote_drafts_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.ForeignKey(
                        name: "FK_vote_drafts_vote_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "vote_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_drafts_vote_candidates_refused_candidate_id",
                        column: x => x.refused_candidate_id,
                        principalTable: "vote_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
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
                    last_revised_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    event_on = table.Column<DateOnly>(type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1)),
                    issue_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    official_ids = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    held_reason = table.Column<string>(type: "text", nullable: true),
                    refused_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_drafts", x => x.record_id);
                    table.UniqueConstraint("ux_action_drafts_record_bill", x => new { x.record_id, x.source_id, x.congress, x.bill_type, x.bill_number });
                    table.CheckConstraint("ck_action_drafts_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_action_drafts_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_drafts_last_revised_at_utc_ticks", "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_drafts_review_state_version", "review_state_version >= 0");
                    table.CheckConstraint("ck_action_drafts_review_status", "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");
                    table.CheckConstraint("ck_action_drafts_revision", "current_revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_action_drafts_action_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "action_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_drafts_action_candidates_refused_candidate_id",
                        column: x => x.refused_candidate_id,
                        principalTable: "action_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_actions",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    action_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    action_code = table.Column<string>(type: "text", nullable: true),
                    source_system_code = table.Column<int>(type: "integer", nullable: true),
                    source_system_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_actions", x => new { x.detail_job_id, x.position });
                    table.CheckConstraint("ck_bill_actions_position", "position >= 0");
                    table.CheckConstraint("ck_bill_actions_text", "text ~ '\\S'");
                    table.ForeignKey(
                        name: "FK_bill_actions_bill_gates_detail_job_id",
                        column: x => x.detail_job_id,
                        principalTable: "bill_gates",
                        principalColumn: "detail_job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bill_actions_bill_pages_job_id",
                        column: x => x.job_id,
                        principalTable: "bill_pages",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_bill_history_actions",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    position = table.Column<int>(type: "integer", nullable: false),
                    action_date = table.Column<DateOnly>(type: "date", nullable: false),
                    action_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: true),
                    action_code = table.Column<string>(type: "text", nullable: true),
                    source_system_code = table.Column<int>(type: "integer", nullable: true),
                    source_system_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_bill_history_actions", x => new { x.detail_job_id, x.projection_version, x.position });
                    table.CheckConstraint("ck_published_bill_history_actions_position", "position >= 0");
                    table.CheckConstraint("ck_published_bill_history_actions_projection_version", "projection_version >= 0");
                    table.CheckConstraint("ck_published_bill_history_actions_text", "text ~ '\\S'");
                    table.ForeignKey(
                        name: "FK_published_bill_history_actions_published_bill_histories_det~",
                        columns: x => new { x.detail_job_id, x.projection_version },
                        principalTable: "published_bill_histories",
                        principalColumns: new[] { "detail_job_id", "projection_version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_bill_histories",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    projection_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_bill_histories", x => new { x.release_number, x.record_id });
                    table.CheckConstraint("ck_release_bill_histories_projection_version", "projection_version >= 0");
                    table.CheckConstraint("ck_release_bill_histories_record_id", "record_id ~ '^[0-9a-f]{64}$'");
                    table.ForeignKey(
                        name: "FK_release_bill_histories_publication_releases_release_number",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_bill_histories_published_bill_histories_detail_job_~",
                        columns: x => new { x.detail_job_id, x.projection_version },
                        principalTable: "published_bill_histories",
                        principalColumns: new[] { "detail_job_id", "projection_version" },
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_document_change_review_decisions_decision_id_draft_id", x => new { x.decision_id, x.draft_id });
                    table.CheckConstraint("ck_document_change_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_document_change_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_document_change_review_decisions_reason_code", "reason_code IN ('InaccurateSummary', 'Duplicate', 'LowSubstance')");
                    table.CheckConstraint("ck_document_change_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_document_change_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_document_change_review_decisions_document_change_revisions_~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revision_citations",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false),
                    is_change_date_evidence = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_citations", x => new { x.draft_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_document_change_revision_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_document_change_revision_citations_length", "citation_length <= 8192");
                    table.CheckConstraint("ck_document_change_revision_citations_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_citations_document_change_revision~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_revision_citations_document_extractions_cit~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revision_issues",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_issues", x => new { x.draft_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_document_change_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_document_change_revision_issues_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_issues_document_change_revisions_d~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_revision_officials",
                columns: table => new
                {
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_revision_officials", x => new { x.draft_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_document_change_revision_officials_official", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_document_change_revision_officials_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_document_change_revision_officials_document_change_revision~",
                        columns: x => new { x.draft_id, x.revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bill_action_recorded_votes",
                columns: table => new
                {
                    detail_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    action_position = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_number = table.Column<int>(type: "integer", nullable: false),
                    voted_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bill_action_recorded_votes", x => new { x.detail_job_id, x.action_position, x.position });
                    table.CheckConstraint("ck_bill_action_recorded_votes_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_bill_action_recorded_votes_position", "action_position >= 0 AND position >= 0");
                    table.CheckConstraint("ck_bill_action_recorded_votes_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_number >= 1");
                    table.CheckConstraint("ck_bill_action_recorded_votes_url", "url IS NULL OR url LIKE 'https://%'");
                    table.CheckConstraint("ck_bill_action_recorded_votes_voted_at_utc_ticks", "voted_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_bill_action_recorded_votes_bill_actions_detail_job_id_actio~",
                        columns: x => new { x.detail_job_id, x.action_position },
                        principalTable: "bill_actions",
                        principalColumns: new[] { "detail_job_id", "position" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_change_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_document_change_review_decision_resolutions_document_change~",
                        columns: x => new { x.decision_id, x.draft_id },
                        principalTable: "document_change_review_decisions",
                        principalColumns: new[] { "decision_id", "draft_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_review_decision_resolutions_document_chang~1",
                        columns: x => new { x.resolved_decision_id, x.draft_id },
                        principalTable: "document_change_review_decisions",
                        principalColumns: new[] { "decision_id", "draft_id" },
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
                    table.CheckConstraint("ck_action_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_action_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_action_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_action_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.PrimaryKey("PK_action_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_action_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_action_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_action_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_action_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_action_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_action_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept','recordRejected')");
                    table.CheckConstraint("ck_action_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_action_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_action_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
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
                    table.ForeignKey(
                        name: "FK_action_analysis_runs_analysis_output_schemas_output_schema_~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_analysis_runs_analysis_system_prompts_system_prompt_~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
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
                    code_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    text_origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    suggestion_headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    suggestion_summary = table.Column<string>(type: "text", nullable: true),
                    suggestion_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    suggestion_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    congress = table.Column<int>(type: "integer", nullable: true),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bill_number = table.Column<int>(type: "integer", nullable: true),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    candidate_update_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    candidate_includes_summaries = table.Column<bool>(type: "boolean", nullable: true),
                    candidate_includes_subjects = table.Column<bool>(type: "boolean", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    summary_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    crs_summary_text = table.Column<string>(type: "text", nullable: true),
                    crs_summary_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    summary_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    summary_citation_start = table.Column<int>(type: "integer", nullable: true),
                    summary_citation_length = table.Column<int>(type: "integer", nullable: true),
                    policy_area = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    policy_area_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    policy_area_citation_start = table.Column<int>(type: "integer", nullable: true),
                    policy_area_citation_length = table.Column<int>(type: "integer", nullable: true),
                    policy_area_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    bill_title = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    bill_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    bill_citation_start = table.Column<int>(type: "integer", nullable: true),
                    bill_citation_length = table.Column<int>(type: "integer", nullable: true),
                    issue_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    subjects_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    issue_candidates_recorded = table.Column<bool>(type: "boolean", nullable: true),
                    event_on = table.Column<DateOnly>(type: "date", nullable: true),
                    cosponsor_count = table.Column<int>(type: "integer", nullable: true),
                    introduced_on = table.Column<DateOnly>(type: "date", nullable: true),
                    text_package_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    why_it_matters = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_action_revisions_author_kind", "author_kind IN ('Person','Analysis','Code')");
                    table.CheckConstraint("ck_action_revisions_bill", "congress >= 1 AND bill_number >= 1");
                    table.CheckConstraint("ck_action_revisions_bill_citation", "(bill_citation_extraction_id IS NULL) = (bill_citation_start IS NULL) AND (bill_citation_extraction_id IS NULL) = (bill_citation_length IS NULL) AND bill_citation_start >= 0 AND bill_citation_length > 0");
                    table.CheckConstraint("ck_action_revisions_candidate", "candidate_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_revisions_candidate_update_utc_ticks", "candidate_update_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_revisions_code_bill_citation", "author_kind <> 'Code' OR headline IS NULL OR bill_citation_extraction_id IS NOT NULL");
                    table.CheckConstraint("ck_action_revisions_code_version", "code_version ~ '^[0-9a-f]{40}$'");
                    table.CheckConstraint("ck_action_revisions_code_version_author", "code_version IS NULL OR author_kind = 'Code'");
                    table.CheckConstraint("ck_action_revisions_columns_written", "num_nulls(headline, summary, text_origin, source_id, congress, bill_type, bill_number, candidate_id, candidate_update_utc_ticks, candidate_includes_summaries, candidate_includes_subjects, fingerprint, bill_title, issue_revision_id, issue_candidates_recorded) IN (0, 15)");
                    table.CheckConstraint("ck_action_revisions_cosponsor_count", "cosponsor_count >= 0");
                    table.CheckConstraint("ck_action_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_revisions_crs_summary", "crs_summary_text IS NULL OR (length(btrim(crs_summary_text)) > 0 AND summary_version IS NOT NULL AND summary_citation_extraction_id IS NOT NULL)");
                    table.CheckConstraint("ck_action_revisions_derived_columns_written", "num_nulls(event_on, cosponsor_count) IN (0, 2)");
                    table.CheckConstraint("ck_action_revisions_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_revisions_headline", "length(btrim(headline)) BETWEEN 1 AND 256");
                    table.CheckConstraint("ck_action_revisions_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_action_revisions_person_text", "author_kind <> 'Person' OR text_origin = 'Person'");
                    table.CheckConstraint("ck_action_revisions_policy_area", "(policy_area IS NULL) = (policy_area_citation_extraction_id IS NULL)");
                    table.CheckConstraint("ck_action_revisions_policy_area_citation", "(policy_area_citation_extraction_id IS NULL) = (policy_area_citation_start IS NULL) AND (policy_area_citation_extraction_id IS NULL) = (policy_area_citation_length IS NULL) AND policy_area_citation_start >= 0 AND policy_area_citation_length > 0");
                    table.CheckConstraint("ck_action_revisions_policy_area_id", "policy_area_id IS NULL OR policy_area IS NOT NULL");
                    table.CheckConstraint("ck_action_revisions_run", "(author_kind <> 'Person') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_action_revisions_subjects_hash", "subjects_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_action_revisions_suggestion", "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");
                    table.CheckConstraint("ck_action_revisions_summary", "length(summary) BETWEEN 1 AND 8000");
                    table.CheckConstraint("ck_action_revisions_summary_citation", "(summary_citation_extraction_id IS NULL) = (summary_citation_start IS NULL) AND (summary_citation_extraction_id IS NULL) = (summary_citation_length IS NULL) AND summary_citation_start >= 0 AND summary_citation_length > 0");
                    table.CheckConstraint("ck_action_revisions_summary_version", "(summary_version IS NULL) = (summary_citation_extraction_id IS NULL)");
                    table.CheckConstraint("ck_action_revisions_text_origin", "text_origin IN ('Model', 'Person', 'Code')");
                    table.CheckConstraint("ck_action_revisions_text_source", "text_package_id IS NULL OR summary_citation_extraction_id IS NULL");
                    table.CheckConstraint("ck_action_revisions_why_it_matters", "(text_package_id IS NULL) = (why_it_matters IS NULL)");
                    table.ForeignKey(
                        name: "FK_action_revisions_action_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "action_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_action_analysis_runs_suggestion_run_id",
                        column: x => x.suggestion_run_id,
                        principalTable: "action_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_action_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "action_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_action_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "action_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_action_drafts_record_id_source_id_congress~",
                        columns: x => new { x.record_id, x.source_id, x.congress, x.bill_type, x.bill_number },
                        principalTable: "action_drafts",
                        principalColumns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_bill_text_versions_congress_bill_type_bill~",
                        columns: x => new { x.congress, x.bill_type, x.bill_number, x.text_package_id },
                        principalTable: "bill_text_versions",
                        principalColumns: new[] { "congress", "bill_type", "bill_number", "package_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_document_extractions_bill_citation_extract~",
                        column: x => x.bill_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_document_extractions_policy_area_citation_~",
                        column: x => x.policy_area_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revisions_document_extractions_summary_citation_extr~",
                        column: x => x.summary_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_action_review_decisions_decision_id_record_id", x => new { x.decision_id, x.record_id });
                    table.CheckConstraint("ck_action_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_action_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_action_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_action_review_decisions_reason_code", "reason_code IN ('InaccurateSummary', 'Duplicate')");
                    table.CheckConstraint("ck_action_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_action_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_action_review_decisions_action_revisions_record_id_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_issues",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    is_tracked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_issues", x => new { x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_action_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_action_revision_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_action_revision_issues_action_revisions_record_id_revision_~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_key_terms",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    term = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cut = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_key_terms", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_key_terms_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_key_terms_position", "position BETWEEN 0 AND 4");
                    table.CheckConstraint("ck_action_revision_key_terms_quote", "citation_length <= 300 AND length(btrim(definition)) > 0");
                    table.CheckConstraint("ck_action_revision_key_terms_term", "length(btrim(term)) > 0");
                    table.ForeignKey(
                        name: "FK_action_revision_key_terms_action_revisions_record_id_revisi~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_key_terms_document_extractions_citation_ext~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_sponsorships",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    person_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    bioguide_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_original = table.Column<bool>(type: "boolean", nullable: false),
                    sponsored_on = table.Column<DateOnly>(type: "date", nullable: false),
                    withdrawn_on = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_sponsorships", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_sponsorships_person", "length(btrim(person_id)) > 0 AND length(btrim(bioguide_id)) > 0");
                    table.CheckConstraint("ck_action_revision_sponsorships_position", "position >= 0");
                    table.CheckConstraint("ck_action_revision_sponsorships_role", "role IN ('Sponsor', 'Cosponsor')");
                    table.CheckConstraint("ck_action_revision_sponsorships_withdrawn", "withdrawn_on >= sponsored_on");
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorships_action_revisions_record_id_rev~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_text_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false),
                    sentence = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_text_citations", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_action_revision_text_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_text_citations_part", "part IN ('Summary', 'WhyItMatters')");
                    table.CheckConstraint("ck_action_revision_text_citations_position", "position BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_action_revision_text_citations_quote", "citation_length <= 300");
                    table.ForeignKey(
                        name: "FK_action_revision_text_citations_action_revisions_record_id_r~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_text_citations_document_extractions_citatio~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_action_review_decision_resolutions_action_review_decisions_~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_review_decision_resolutions_action_review_decisions~1",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "action_revision_sponsorship_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    sponsorship_position = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_action_revision_sponsorship_citations", x => new { x.record_id, x.revision_number, x.sponsorship_position, x.position });
                    table.CheckConstraint("ck_action_revision_sponsorship_citations_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_action_revision_sponsorship_citations_position", "position BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorship_citations_action_revision_spons~",
                        columns: x => new { x.record_id, x.revision_number, x.sponsorship_position },
                        principalTable: "action_revision_sponsorships",
                        principalColumns: new[] { "record_id", "revision_number", "position" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_action_revision_sponsorship_citations_document_extractions_~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
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
                    table.CheckConstraint("ck_brief_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_brief_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_brief_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_brief_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_brief_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                    table.CheckConstraint("ck_brief_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                    table.CheckConstraint("ck_brief_analysis_week", "EXTRACT(ISODOW FROM week_start) = 1");
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
                    checked_revision_number = table.Column<int>(type: "integer", nullable: true),
                    recommendation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    input_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
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
                    table.PrimaryKey("PK_brief_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_brief_analysis_runs_check", "(checked_revision_number IS NULL) = (recommendation IS NULL) AND (checked_revision_number IS NULL OR (checked_revision_number >= 1 AND task = 'brief-check' AND recommendation IN ('ApproveRecommended','NeedsAttention'))) AND (source_run_id IS NOT NULL) = (task = 'brief-check' AND outcome = 'Bypassed')");
                    table.CheckConstraint("ck_brief_analysis_runs_fingerprint", "(input_fingerprint IS NOT NULL) = (task = 'brief-check' AND outcome = 'Drafted') AND (input_fingerprint IS NULL OR input_fingerprint ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_brief_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_brief_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_brief_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_brief_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_brief_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_brief_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noNewRecords')");
                    table.CheckConstraint("ck_brief_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_brief_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_brief_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_brief_analysis_runs_analysis_output_schemas_output_schema_s~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_analysis_runs_analysis_system_prompts_system_prompt_s~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
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
                    table.ForeignKey(
                        name: "FK_brief_analysis_runs_brief_analysis_runs_source_run_id",
                        column: x => x.source_run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_check_flags",
                columns: table => new
                {
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    flag_number = table.Column<int>(type: "integer", nullable: false),
                    paragraph_index = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    citation_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_check_flags", x => new { x.run_id, x.flag_number });
                    table.CheckConstraint("ck_brief_check_flags_code", "code IN ('UnsupportedByCitation','Overstated','StatesBillStatus','LoadedWording','OmitsNotableRecord','Other')");
                    table.CheckConstraint("ck_brief_check_flags_message", "char_length(message) BETWEEN 1 AND 300");
                    table.CheckConstraint("ck_brief_check_flags_number", "flag_number BETWEEN 1 AND 24");
                    table.CheckConstraint("ck_brief_check_flags_paragraph", "paragraph_index BETWEEN 0 AND 11");
                    table.ForeignKey(
                        name: "FK_brief_check_flags_brief_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: true),
                    window_start_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    window_end_utc_ticks = table.Column<long>(type: "bigint", nullable: true),
                    first_release_number = table.Column<int>(type: "integer", nullable: true),
                    cutoff_release_number = table.Column<int>(type: "integer", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    text_origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    suggestion_headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    suggestion_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    suggestion_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    coverage_considered = table.Column<int>(type: "integer", nullable: true),
                    coverage_sent_to_model = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_brief_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_brief_revisions_columns_written", "num_nulls(week_start, window_start_utc_ticks, window_end_utc_ticks, first_release_number, cutoff_release_number, fingerprint, headline, text_origin, coverage_considered, coverage_sent_to_model) IN (0, 10)");
                    table.CheckConstraint("ck_brief_revisions_coverage", "coverage_sent_to_model >= 0 AND coverage_sent_to_model <= coverage_considered");
                    table.CheckConstraint("ck_brief_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_revisions_fingerprint", "fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_brief_revisions_headline", "length(btrim(headline)) BETWEEN 1 AND 256");
                    table.CheckConstraint("ck_brief_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_brief_revisions_person_text", "author_kind <> 'Person' OR text_origin = 'Person'");
                    table.CheckConstraint("ck_brief_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_brief_revisions_suggestion", "(suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL) AND length(btrim(suggestion_headline)) BETWEEN 1 AND 256 AND suggestion_run_id ~ '^[0-9a-f]{32}$' AND suggestion_fingerprint ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_brief_revisions_text_origin", "text_origin IN ('Model', 'Person', 'Code')");
                    table.CheckConstraint("ck_brief_revisions_window", "window_end_utc_ticks - window_start_utc_ticks BETWEEN 6012000000000 AND 6084000000000 AND first_release_number >= 1 AND first_release_number <= cutoff_release_number");
                    table.CheckConstraint("ck_brief_revisions_window_end_utc_ticks", "window_end_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_revisions_window_start_utc_ticks", "window_start_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_analysis_runs_suggestion_run_id",
                        column: x => x.suggestion_run_id,
                        principalTable: "brief_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "brief_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revisions_brief_drafts_record_id_week_start",
                        columns: x => new { x.record_id, x.week_start },
                        principalTable: "brief_drafts",
                        principalColumns: new[] { "record_id", "week_start" },
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_brief_review_decisions_decision_id_record_id", x => new { x.decision_id, x.record_id });
                    table.CheckConstraint("ck_brief_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_brief_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_brief_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_brief_review_decisions_reason_code", "reason_code IN ('InaccurateSummary', 'Duplicate', 'LowSubstance')");
                    table.CheckConstraint("ck_brief_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_brief_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_brief_review_decisions_brief_revisions_record_id_revision_n~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revision_paragraphs",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    paragraph_text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_paragraphs", x => new { x.record_id, x.revision_number, x.part, x.position });
                    table.CheckConstraint("ck_brief_revision_paragraphs_part", "part IN ('Text', 'Suggestion')");
                    table.CheckConstraint("ck_brief_revision_paragraphs_position", "position BETWEEN 0 AND 11");
                    table.CheckConstraint("ck_brief_revision_paragraphs_text", "length(btrim(paragraph_text)) BETWEEN 1 AND 4000");
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraphs_brief_revisions_record_id_revisio~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_brief_review_decision_resolutions_brief_review_decisions_de~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "brief_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_review_decision_resolutions_brief_review_decisions_re~",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "brief_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revision_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    citation_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_revision_number = table.Column<int>(type: "integer", nullable: true),
                    action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    document_change_draft_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    document_change_revision_number = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_citations", x => new { x.record_id, x.revision_number, x.part, x.citation_id });
                    table.CheckConstraint("ck_brief_revision_citations_cited", "(kind = 'Statement') = (statement_candidate_id IS NOT NULL) AND (statement_candidate_id IS NULL) = (statement_revision_number IS NULL) AND (kind = 'Action') = (action_record_id IS NOT NULL) AND (action_record_id IS NULL) = (action_revision_number IS NULL) AND (kind = 'DocumentChange') = (document_change_draft_id IS NOT NULL) AND (document_change_draft_id IS NULL) = (document_change_revision_number IS NULL) AND coalesce(statement_revision_number, action_revision_number, document_change_revision_number) >= 1");
                    table.CheckConstraint("ck_brief_revision_citations_id", "citation_id ~ '^R[1-9][0-9]{0,3}$'");
                    table.CheckConstraint("ck_brief_revision_citations_kind", "kind IN ('DocumentChange', 'Statement', 'Action')");
                    table.CheckConstraint("ck_brief_revision_citations_part", "part IN ('Text', 'Suggestion')");
                    table.CheckConstraint("ck_brief_revision_citations_position", "position BETWEEN 0 AND 255");
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_action_revisions_action_record_id_~",
                        columns: x => new { x.action_record_id, x.action_revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_brief_revisions_record_id_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_citations_document_change_revisions_document~",
                        columns: x => new { x.document_change_draft_id, x.document_change_revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brief_revision_paragraph_citations",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    part = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paragraph_position = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_id = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brief_revision_paragraph_citations", x => new { x.record_id, x.revision_number, x.part, x.paragraph_position, x.position });
                    table.CheckConstraint("ck_brief_revision_paragraph_citations_position", "position BETWEEN 0 AND 255");
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraph_citations_brief_revision_citations~",
                        columns: x => new { x.record_id, x.revision_number, x.part, x.citation_id },
                        principalTable: "brief_revision_citations",
                        principalColumns: new[] { "record_id", "revision_number", "part", "citation_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_brief_revision_paragraph_citations_brief_revision_paragraph~",
                        columns: x => new { x.record_id, x.revision_number, x.part, x.paragraph_position },
                        principalTable: "brief_revision_paragraphs",
                        principalColumns: new[] { "record_id", "revision_number", "part", "position" },
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
                    table.CheckConstraint("ck_document_change_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_document_change_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_document_change_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_document_change_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    system_prompt_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    output_schema_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_message = table.Column<string>(type: "text", nullable: true),
                    effort = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    revision_number = table.Column<int>(type: "integer", nullable: true),
                    started_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    finished_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_change_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_document_change_analysis_runs_draft", "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number >= 1)");
                    table.CheckConstraint("ck_document_change_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_document_change_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_document_change_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_document_change_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_document_change_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted')");
                    table.CheckConstraint("ck_document_change_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_document_change_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_document_change_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_document_change_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_document_change_analysis_runs_analysis_output_schemas_outpu~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_document_change_analysis_runs_analysis_system_prompts_syste~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "executive_action_revisions",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    revision_json = table.Column<string>(type: "text", nullable: false),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    code_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    document_number = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    presidential_number = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    published_on = table.Column<DateOnly>(type: "date", nullable: true),
                    federal_register_citation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    president_name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    person_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    document_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    disposition_notes = table.Column<string>(type: "character varying(1999981)", maxLength: 1999981, nullable: true),
                    disposition_notes_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    disposition_notes_start = table.Column<int>(type: "integer", nullable: true),
                    disposition_notes_length = table.Column<int>(type: "integer", nullable: true),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    summary_text_origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    summary_cut = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_executive_action_revisions_author", "author_kind = 'Code' AND author_subject = 'civic-lens:code' OR author_kind = 'Analysis' AND author_subject = 'civic-lens:analysis'");
                    table.CheckConstraint("ck_executive_action_revisions_code_version", "code_version ~ '^[0-9a-f]{40}$'");
                    table.CheckConstraint("ck_executive_action_revisions_columns_written", "num_nulls(source_id, document_number, kind, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url) IN (0, 10)");
                    table.CheckConstraint("ck_executive_action_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_executive_action_revisions_dates", "signed_on <> '0001-01-01' AND published_on <> '0001-01-01'");
                    table.CheckConstraint("ck_executive_action_revisions_disposition_notes", "(disposition_notes_extraction_id IS NULL) = (disposition_notes_start IS NULL) AND (disposition_notes_extraction_id IS NULL) = (disposition_notes_length IS NULL) AND disposition_notes_start >= 0 AND disposition_notes_length > 0");
                    table.CheckConstraint("ck_executive_action_revisions_disposition_notes_written", "(disposition_notes IS NULL) = (disposition_notes_extraction_id IS NULL)");
                    table.CheckConstraint("ck_executive_action_revisions_fields", "(document_number IS NULL OR (btrim(document_number) <> '' AND length(document_number) <= 1000 AND document_number !~ '[\\r\\n]')) AND (title IS NULL OR (btrim(title) <> '' AND length(title) <= 1000 AND title !~ '[\\r\\n]')) AND (federal_register_citation IS NULL OR (btrim(federal_register_citation) <> '' AND length(federal_register_citation) <= 1000 AND federal_register_citation !~ '[\\r\\n]')) AND (president_name IS NULL OR (btrim(president_name) <> '' AND length(president_name) <= 1000 AND president_name !~ '[\\r\\n]')) AND (document_url IS NULL OR (btrim(document_url) <> '' AND length(document_url) <= 1000 AND document_url !~ '[\\r\\n]')) AND (presidential_number IS NULL OR (btrim(presidential_number) <> '' AND length(presidential_number) <= 1000 AND presidential_number !~ '[\\r\\n]'))");
                    table.CheckConstraint("ck_executive_action_revisions_ids", "length(btrim(source_id)) > 0 AND length(btrim(person_id)) > 0");
                    table.CheckConstraint("ck_executive_action_revisions_kind", "kind IN ('ExecutiveOrder', 'Proclamation', 'Notice', 'Memorandum')");
                    table.CheckConstraint("ck_executive_action_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_executive_action_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_executive_action_revisions_summary_analysis", "author_kind <> 'Analysis' OR summary_text_origin IS NOT NULL");
                    table.CheckConstraint("ck_executive_action_revisions_summary_origin", "summary_text_origin IN ('Model')");
                    table.CheckConstraint("ck_executive_action_revisions_summary_written", "(summary_text_origin IS NULL) = (summary_cut IS NULL)");
                    table.ForeignKey(
                        name: "FK_executive_action_revisions_document_extractions_disposition~",
                        column: x => x.disposition_notes_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revisions_exec_summary_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "exec_summary_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
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
                    revision_number = table.Column<int>(type: "integer", nullable: true),
                    notice_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notice_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notice_citation_start = table.Column<int>(type: "integer", nullable: true),
                    notice_citation_length = table.Column<int>(type: "integer", nullable: true),
                    published_on = table.Column<DateOnly>(type: "date", nullable: false),
                    body_html_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    document_job_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_documents", x => new { x.source_id, x.document_number });
                    table.CheckConstraint("ck_executive_action_documents_notice", "(notice_url IS NULL) = (notice_citation_extraction_id IS NULL) AND (notice_url IS NULL OR btrim(notice_url) <> '' AND length(notice_url) <= 1000)");
                    table.CheckConstraint("ck_executive_action_documents_notice_citation", "(notice_citation_extraction_id IS NULL) = (notice_citation_start IS NULL) AND (notice_citation_extraction_id IS NULL) = (notice_citation_length IS NULL) AND notice_citation_start >= 0 AND notice_citation_length > 0");
                    table.CheckConstraint("ck_executive_action_documents_pending", "revision_number IS NOT NULL OR notice_url IS NOT NULL");
                    table.CheckConstraint("ck_executive_action_documents_revision", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_executive_action_documents_collection_jobs_document_job_id",
                        column: x => x.document_job_id,
                        principalTable: "collection_jobs",
                        principalColumn: "job_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_documents_document_extractions_notice_cita~",
                        column: x => x.notice_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_executive_action_review_decisions_decision_id_record_id", x => new { x.decision_id, x.record_id });
                    table.CheckConstraint("ck_executive_action_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_executive_action_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_executive_action_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_executive_action_review_decisions_reason_code", "reason_code IN ('InaccurateSummary', 'Duplicate')");
                    table.CheckConstraint("ck_executive_action_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_executive_action_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_executive_action_review_decisions_executive_action_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_revision_disposition_targets",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    number = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    reference_start = table.Column<int>(type: "integer", nullable: false),
                    reference_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_disposition_targets", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_disposition_targets_kind", "kind IN ('ExecutiveOrder', 'Proclamation')");
                    table.CheckConstraint("ck_executive_action_revision_disposition_targets_number", "number ~ '^[1-9][0-9]{0,5}$'");
                    table.CheckConstraint("ck_executive_action_revision_disposition_targets_position", "position >= 0");
                    table.CheckConstraint("ck_executive_action_revision_disposition_targets_relation", "relation IN ('Amends', 'Revokes')");
                    table.CheckConstraint("ck_executive_action_revision_disposition_targets_span", "reference_start >= 0 AND reference_length > 0");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_disposition_targets_executive_act~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_revision_facts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_facts", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_facts_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_executive_action_revision_facts_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_facts_document_extractions_citati~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revision_facts_executive_action_revisions_~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "executive_action_revision_notices",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_revision_notices", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_executive_action_revision_notices_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_executive_action_revision_notices_executive_action_document~",
                        columns: x => new { x.source_id, x.document_number },
                        principalTable: "executive_action_documents",
                        principalColumns: new[] { "source_id", "document_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_revision_notices_executive_action_revision~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_texts",
                columns: table => new
                {
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    document_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chars_total = table.Column<int>(type: "integer", nullable: false),
                    chars_read = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_texts", x => new { x.source_id, x.document_number });
                    table.CheckConstraint("ck_executive_action_texts_chars", "chars_read >= 1 AND chars_read <= 60000 AND chars_read <= chars_total AND (chars_read = chars_total OR chars_total > 60000)");
                    table.ForeignKey(
                        name: "FK_executive_action_texts_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_texts_executive_action_documents_source_id~",
                        columns: x => new { x.source_id, x.document_number },
                        principalTable: "executive_action_documents",
                        principalColumns: new[] { "source_id", "document_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "executive_action_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executive_action_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_executive_action_review_decision_resolutions_executive_acti~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "executive_action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_executive_action_review_decision_resolutions_executive_act~1",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "executive_action_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entries",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    brief_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    brief_revision_number = table.Column<int>(type: "integer", nullable: true),
                    executive_action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    executive_action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    relationship_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    relationship_revision_number = table.Column<int>(type: "integer", nullable: true),
                    statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_revision_number = table.Column<int>(type: "integer", nullable: true),
                    document_change_draft_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    document_change_revision_number = table.Column<int>(type: "integer", nullable: true),
                    vote_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    vote_revision_number = table.Column<int>(type: "integer", nullable: true),
                    headline = table.Column<string>(type: "text", nullable: false),
                    source_host = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    bill_title = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    link_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    statement_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true, computedColumnSql: "CASE WHEN statement_record_id IS NULL THEN NULL ELSE 'statement' END", stored: true),
                    target_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    target_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    document_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    bill_line = table.Column<string>(type: "text", nullable: true),
                    congress = table.Column<int>(type: "integer", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    approved_at_utc_ticks = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entries", x => new { x.kind, x.record_id, x.revision_number });
                    table.CheckConstraint("ck_published_entries_approved_at_utc_ticks", "approved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_published_entries_approved_by", "approved_by IS NULL OR (kind IN ('statement', 'relationship') AND approved_by IN ('Code', 'Person'))");
                    table.CheckConstraint("ck_published_entries_bill_line", "bill_line IS NULL OR (kind = 'action' AND length(btrim(bill_line)) >= 1 AND length(bill_line) <= 200)");
                    table.CheckConstraint("ck_published_entries_bill_title", "(kind = 'action') = (bill_title IS NOT NULL) AND (bill_title IS NULL OR length(btrim(bill_title)) > 0)");
                    table.CheckConstraint("ck_published_entries_congress", "congress IS NULL OR congress >= 1");
                    table.CheckConstraint("ck_published_entries_document_type", "(kind = 'executiveAction') = (document_type IS NOT NULL) AND (document_type IS NULL OR document_type IN ('ExecutiveOrder', 'Proclamation', 'Notice', 'Memorandum'))");
                    table.CheckConstraint("ck_published_entries_headline", "length(btrim(headline)) > 0");
                    table.CheckConstraint("ck_published_entries_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_published_entries_link", "(kind = 'relationship') = (link_type IS NOT NULL) AND num_nonnulls(link_type, statement_record_id, target_kind, target_record_id) IN (0, 4) AND (link_type IS NULL OR (link_type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor', 'StatementAboutVote') AND target_kind IN ('action', 'vote') AND statement_record_id ~ '^[0-9a-f]{64}$' AND target_record_id ~ '^[0-9a-f]{64}$' AND (target_kind = 'vote') = (link_type = 'StatementAboutVote')))");
                    table.CheckConstraint("ck_published_entries_record_id", "record_id ~ CASE WHEN kind = 'documentChange' THEN '^[0-9a-f]{32}$' ELSE '^[0-9a-f]{64}$' END");
                    table.CheckConstraint("ck_published_entries_revision_key", "(kind = 'documentChange' AND document_change_draft_id IS NOT DISTINCT FROM record_id AND document_change_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'statement' AND statement_candidate_id IS NOT DISTINCT FROM record_id AND statement_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'action' AND action_record_id IS NOT DISTINCT FROM record_id AND action_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'brief' AND brief_record_id IS NOT DISTINCT FROM record_id AND brief_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'vote' AND vote_record_id IS NOT DISTINCT FROM record_id AND vote_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, relationship_record_id, relationship_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'relationship' AND relationship_record_id IS NOT DISTINCT FROM record_id AND relationship_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, executive_action_record_id, executive_action_revision_number) = 0) OR (kind = 'executiveAction' AND executive_action_record_id IS NOT DISTINCT FROM record_id AND executive_action_revision_number IS NOT DISTINCT FROM revision_number AND num_nonnulls(document_change_draft_id, document_change_revision_number, statement_candidate_id, statement_revision_number, action_record_id, action_revision_number, brief_record_id, brief_revision_number, vote_record_id, vote_revision_number, relationship_record_id, relationship_revision_number) = 0)");
                    table.CheckConstraint("ck_published_entries_revision_number", "revision_number >= 1");
                    table.CheckConstraint("ck_published_entries_source_host", "source_host IS NULL OR length(btrim(source_host)) > 0");
                    table.ForeignKey(
                        name: "FK_published_entries_action_revisions_action_record_id_action_~",
                        columns: x => new { x.action_record_id, x.action_revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_brief_revisions_brief_record_id_brief_rev~",
                        columns: x => new { x.brief_record_id, x.brief_revision_number },
                        principalTable: "brief_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_document_change_revisions_document_change~",
                        columns: x => new { x.document_change_draft_id, x.document_change_revision_number },
                        principalTable: "document_change_revisions",
                        principalColumns: new[] { "draft_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_executive_action_revisions_executive_acti~",
                        columns: x => new { x.executive_action_record_id, x.executive_action_revision_number },
                        principalTable: "executive_action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_kind_record_id",
                        columns: x => new { x.kind, x.record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_statement_kind_statemen~",
                        columns: x => new { x.statement_kind, x.statement_record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_published_entries_published_records_target_kind_target_reco~",
                        columns: x => new { x.target_kind, x.target_record_id },
                        principalTable: "published_records",
                        principalColumns: new[] { "kind", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_issues",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_issues", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_issues_kind_record_id_revision_number_issue~", x => new { x.kind, x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_published_entry_issues_issue_id", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_published_entry_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_issues_published_entries_kind_record_id_rev~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_officials",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    official_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_matched = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_officials", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_officials_kind_record_id_revision_number_of~", x => new { x.kind, x.record_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_published_entry_officials_matched_kind", "NOT is_matched OR kind = 'relationship'");
                    table.CheckConstraint("ck_published_entry_officials_official_id", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_published_entry_officials_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_officials_published_entries_kind_record_id_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "published_entry_policy_areas",
                columns: table => new
                {
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_entry_policy_areas", x => new { x.kind, x.record_id, x.revision_number, x.position });
                    table.UniqueConstraint("AK_published_entry_policy_areas_kind_record_id_revision_number~", x => new { x.kind, x.record_id, x.revision_number, x.policy_area_id });
                    table.CheckConstraint("ck_published_entry_policy_areas_policy_area_id", "length(btrim(policy_area_id)) > 0");
                    table.CheckConstraint("ck_published_entry_policy_areas_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_published_entry_policy_areas_published_entries_kind_record_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "release_correction_histories",
                columns: table => new
                {
                    release_number = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    history_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_correction_histories", x => new { x.release_number, x.kind, x.record_id });
                    table.CheckConstraint("ck_release_correction_histories_history_id", "history_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_release_correction_histories_kind", "kind IN ('documentChange', 'statement', 'action', 'brief', 'vote', 'relationship', 'executiveAction')");
                    table.CheckConstraint("ck_release_correction_histories_record_id", "record_id ~ CASE WHEN kind = 'documentChange' THEN '^[0-9a-f]{32}$' ELSE '^[0-9a-f]{64}$' END");
                    table.CheckConstraint("ck_release_correction_histories_revision_number", "revision_number >= 1");
                    table.ForeignKey(
                        name: "FK_release_correction_histories_publication_releases_release_n~",
                        column: x => x.release_number,
                        principalTable: "publication_releases",
                        principalColumn: "release_number",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_correction_histories_published_correction_histories~",
                        column: x => x.history_id,
                        principalTable: "published_correction_histories",
                        principalColumn: "history_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_release_correction_histories_published_entries_kind_record_~",
                        columns: x => new { x.kind, x.record_id, x.revision_number },
                        principalTable: "published_entries",
                        principalColumns: new[] { "kind", "record_id", "revision_number" },
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
                    table.CheckConstraint("ck_relationship_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_relationship_analysis_pair", "pair_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_relationship_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_relationship_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_relationship_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.PrimaryKey("PK_relationship_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_relationship_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_relationship_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_relationship_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_relationship_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_relationship_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_relationship_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noRelationship','noLongerMatches','targetRecited')");
                    table.CheckConstraint("ck_relationship_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_relationship_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_relationship_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_relationship_analysis_runs_analysis_output_schemas_output_s~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relationship_analysis_runs_analysis_system_prompts_system_p~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
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
                name: "relationship_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                });

            migrationBuilder.CreateTable(
                name: "relationship_review_decisions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    review_state_version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_relationship_review_decisions_decision_id_record_id", x => new { x.decision_id, x.record_id });
                    table.CheckConstraint("ck_relationship_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_relationship_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_relationship_review_decisions_reason_code", "reason_code IN ('NotRelated', 'Duplicate')");
                    table.CheckConstraint("ck_relationship_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_relationship_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    statement_candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_revision_number = table.Column<int>(type: "integer", nullable: true),
                    target_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    target_action_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_action_revision_number = table.Column<int>(type: "integer", nullable: true),
                    target_vote_record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_vote_revision_number = table.Column<int>(type: "integer", nullable: true),
                    match_basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    match_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    match_start = table.Column<int>(type: "integer", nullable: true),
                    match_length = table.Column<int>(type: "integer", nullable: true),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    statement_quote_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_quote_start = table.Column<int>(type: "integer", nullable: true),
                    statement_quote_length = table.Column<int>(type: "integer", nullable: true),
                    target_fact_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    official_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_relationship_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_relationship_revisions_author_kind", "author_kind IN ('Person','Analysis')");
                    table.CheckConstraint("ck_relationship_revisions_columns_written", "num_nulls(statement_candidate_id, statement_revision_number, target_kind, match_basis, match_extraction_id, match_start, match_length, type, reason, statement_quote_extraction_id, statement_quote_start, statement_quote_length, target_fact_id, official_id) IN (0, 14)");
                    table.CheckConstraint("ck_relationship_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_relationship_revisions_fact_id", "btrim(target_fact_id) <> '' AND length(target_fact_id) <= 16");
                    table.CheckConstraint("ck_relationship_revisions_match", "match_start >= 0 AND match_length > 0");
                    table.CheckConstraint("ck_relationship_revisions_match_basis", "match_basis IN ('BillNumber', 'BillTitle')");
                    table.CheckConstraint("ck_relationship_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_relationship_revisions_official_id", "btrim(official_id) <> '' AND length(official_id) <= 128");
                    table.CheckConstraint("ck_relationship_revisions_quote", "statement_quote_length <= 4096 AND statement_quote_extraction_id = match_extraction_id\nAND statement_quote_start <= match_start AND match_start + match_length <= statement_quote_start + statement_quote_length");
                    table.CheckConstraint("ck_relationship_revisions_reason", "btrim(reason) <> '' AND length(reason) <= 400 AND reason !~ '[\\r\\n]'");
                    table.CheckConstraint("ck_relationship_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_relationship_revisions_statement", "statement_candidate_id ~ '^[0-9a-f]{64}$' AND statement_revision_number >= 1");
                    table.CheckConstraint("ck_relationship_revisions_statement_quote", "statement_quote_start >= 0 AND statement_quote_length > 0");
                    table.CheckConstraint("ck_relationship_revisions_target", "target_kind IS NULL AND num_nonnulls(target_action_record_id, target_action_revision_number, target_vote_record_id, target_vote_revision_number) = 0\nOR target_kind = 'Action' AND num_nonnulls(target_action_record_id, target_action_revision_number) = 2 AND num_nonnulls(target_vote_record_id, target_vote_revision_number) = 0\nOR target_kind = 'Vote' AND num_nonnulls(target_vote_record_id, target_vote_revision_number) = 2 AND num_nonnulls(target_action_record_id, target_action_revision_number) = 0");
                    table.CheckConstraint("ck_relationship_revisions_target_ids", "target_action_record_id ~ '^[0-9a-f]{64}$' AND target_action_revision_number >= 1 AND target_vote_record_id ~ '^[0-9a-f]{64}$' AND target_vote_revision_number >= 1");
                    table.CheckConstraint("ck_relationship_revisions_type", "type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor', 'StatementAboutVote')");
                    table.CheckConstraint("ck_relationship_revisions_type_target", "target_kind = 'Action' AND type IN ('DescribesOwnBill', 'StatesPositionOn', 'ClaimsCreditFor') OR target_kind = 'Vote' AND type IN ('StatementAboutVote')");
                    table.ForeignKey(
                        name: "FK_relationship_revisions_action_revisions_target_action_recor~",
                        columns: x => new { x.target_action_record_id, x.target_action_revision_number },
                        principalTable: "action_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relationship_revisions_document_extractions_match_extractio~",
                        column: x => x.match_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_relationship_revisions_document_extractions_statement_quote~",
                        column: x => x.statement_quote_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
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
                    table.CheckConstraint("ck_statement_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_statement_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_statement_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_statement_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.PrimaryKey("PK_statement_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_statement_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (issue_revision_id IS NULL OR issue_revision_id ~ '^[0-9a-f]{64}$') AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_statement_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_statement_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_statement_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_statement_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Classified','Bypassed')");
                    table.CheckConstraint("ck_statement_analysis_runs_reason", "reason IS NULL OR reason IN ('passed','passed-bill-or-vote','passed-bill-number','no-tracked-issue','over-byte-cap','gate-failed-open','source-text')");
                    table.CheckConstraint("ck_statement_analysis_runs_revision", "issue_revision_id IS NOT NULL OR outcome = 'Interrupted' OR task <> 'gate'");
                    table.CheckConstraint("ck_statement_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_statement_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_statement_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_statement_analysis_runs_analysis_output_schemas_output_sche~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_analysis_runs_analysis_system_prompts_system_prom~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
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
                    table.CheckConstraint("ck_statement_gate_labels_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_gate_labels_id", "label_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_statement_gate_labels_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_statement_gate_labels_document_extractions_extraction_id",
                        column: x => x.extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
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
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    statement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    statement_date_evidence_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    statement_date_evidence_start = table.Column<int>(type: "integer", nullable: true),
                    statement_date_evidence_length = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revisions", x => new { x.candidate_id, x.revision_number });
                    table.CheckConstraint("ck_statement_revisions_author_kind", "author_kind IN ('Person','Analysis','Code')");
                    table.CheckConstraint("ck_statement_revisions_code_date", "author_kind <> 'Code' OR statement_date IS NULL");
                    table.CheckConstraint("ck_statement_revisions_columns_written", "num_nulls(headline, summary) IN (0, 2)");
                    table.CheckConstraint("ck_statement_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_revisions_date_evidence", "(statement_date IS NULL) = (statement_date_evidence_extraction_id IS NULL)");
                    table.CheckConstraint("ck_statement_revisions_date_evidence_length", "statement_date_evidence_length <= 4096");
                    table.CheckConstraint("ck_statement_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_statement_revisions_run", "(author_kind <> 'Person') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_statement_revisions_statement_date_evidence", "(statement_date_evidence_extraction_id IS NULL) = (statement_date_evidence_start IS NULL) AND (statement_date_evidence_extraction_id IS NULL) = (statement_date_evidence_length IS NULL) AND statement_date_evidence_start >= 0 AND statement_date_evidence_length > 0");
                    table.CheckConstraint("ck_statement_revisions_summary", "length(summary) <= 8000");
                    table.ForeignKey(
                        name: "FK_statement_revisions_document_extractions_statement_date_evi~",
                        column: x => x.statement_date_evidence_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revisions_statement_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "statement_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revisions_statement_candidates_candidate_id_state~",
                        columns: x => new { x.candidate_id, x.statement_date_evidence_extraction_id },
                        principalTable: "statement_candidates",
                        principalColumns: new[] { "candidate_id", "extraction_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revisions_statement_drafts_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "statement_drafts",
                        principalColumn: "candidate_id",
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_statement_review_decisions_decision_id_candidate_id", x => new { x.decision_id, x.candidate_id });
                    table.CheckConstraint("ck_statement_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_statement_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_statement_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_statement_review_decisions_reason_code", "reason_code IN ('NotAStatement', 'WrongSpeaker', 'IndexPage', 'LowSubstance', 'InaccurateSummary', 'Duplicate')");
                    table.CheckConstraint("ck_statement_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_statement_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_statement_review_decisions_statement_revisions_candidate_id~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_bill_numbers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    bill_number = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_bill_numbers", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_bill_numbers_bill_number", "length(btrim(bill_number)) > 0");
                    table.CheckConstraint("ck_statement_revision_bill_numbers_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_statement_revision_bill_numbers_statement_revisions_candida~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_issues",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_issues", x => new { x.candidate_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_statement_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_issues_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_issues_statement_revisions_candidate_id_~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_other_speakers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    speaker_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_other_speakers", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_other_speakers_name", "length(speaker_name) BETWEEN 1 AND 128 AND speaker_name = btrim(speaker_name)");
                    table.CheckConstraint("ck_statement_revision_other_speakers_position", "position BETWEEN 0 AND 15");
                    table.ForeignKey(
                        name: "FK_statement_revision_other_speakers_statement_revisions_candi~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_policy_areas",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    policy_area_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_policy_areas", x => new { x.candidate_id, x.revision_number, x.policy_area_id });
                    table.CheckConstraint("ck_statement_revision_policy_areas_policy_area", "length(btrim(policy_area_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_policy_areas_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_policy_areas_statement_revisions_candida~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_quotations",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    quotation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    quotation_start = table.Column<int>(type: "integer", nullable: false),
                    quotation_length = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_quotations", x => new { x.candidate_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_statement_revision_quotations_kind", "kind IN ('DirectQuotation', 'ReleaseText')");
                    table.CheckConstraint("ck_statement_revision_quotations_length", "quotation_length <= 4096");
                    table.CheckConstraint("ck_statement_revision_quotations_position", "position BETWEEN 0 AND 31");
                    table.CheckConstraint("ck_statement_revision_quotations_quotation", "quotation_start >= 0 AND quotation_length > 0");
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_document_extractions_quotatio~",
                        column: x => x.quotation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_statement_candidates_candidat~",
                        columns: x => new { x.candidate_id, x.quotation_extraction_id },
                        principalTable: "statement_candidates",
                        principalColumns: new[] { "candidate_id", "extraction_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_revision_quotations_statement_revisions_candidate~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_revision_speakers",
                columns: table => new
                {
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    official_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_revision_speakers", x => new { x.candidate_id, x.revision_number, x.official_id });
                    table.CheckConstraint("ck_statement_revision_speakers_official", "length(btrim(official_id)) > 0");
                    table.CheckConstraint("ck_statement_revision_speakers_position", "position BETWEEN 0 AND 63");
                    table.ForeignKey(
                        name: "FK_statement_revision_speakers_statement_revisions_candidate_i~",
                        columns: x => new { x.candidate_id, x.revision_number },
                        principalTable: "statement_revisions",
                        principalColumns: new[] { "candidate_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "statement_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_statement_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_statement_review_decision_resolutions_statement_review_deci~",
                        columns: x => new { x.decision_id, x.candidate_id },
                        principalTable: "statement_review_decisions",
                        principalColumns: new[] { "decision_id", "candidate_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_statement_review_decision_resolutions_statement_review_dec~1",
                        columns: x => new { x.resolved_decision_id, x.candidate_id },
                        principalTable: "statement_review_decisions",
                        principalColumns: new[] { "decision_id", "candidate_id" },
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
                    table.CheckConstraint("ck_vote_analysis_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                    table.CheckConstraint("ck_vote_analysis_priority", "priority BETWEEN 1 AND 3");
                    table.CheckConstraint("ck_vote_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                    table.CheckConstraint("ck_vote_analysis_reserved_at_utc_ticks", "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
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
                    table.PrimaryKey("PK_vote_analysis_runs", x => x.run_id);
                    table.CheckConstraint("ck_vote_analysis_runs_finished_at_utc_ticks", "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_analysis_runs_ids", "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                    table.CheckConstraint("ck_vote_analysis_runs_input", "num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)");
                    table.CheckConstraint("ck_vote_analysis_runs_input_outcome", "outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL");
                    table.CheckConstraint("ck_vote_analysis_runs_max_output_tokens", "max_output_tokens IS NULL OR max_output_tokens > 0");
                    table.CheckConstraint("ck_vote_analysis_runs_outcome", "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
                    table.CheckConstraint("ck_vote_analysis_runs_reason", "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged','recordRejected','codeWritten','textKept')");
                    table.CheckConstraint("ck_vote_analysis_runs_started_at_utc_ticks", "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
                    table.CheckConstraint("ck_vote_analysis_runs_tokens", "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
                    table.CheckConstraint("ck_vote_analysis_runs_user_message", "user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000");
                    table.ForeignKey(
                        name: "FK_vote_analysis_runs_analysis_output_schemas_output_schema_sh~",
                        column: x => x.output_schema_sha256,
                        principalTable: "analysis_output_schemas",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_analysis_runs_analysis_system_prompts_system_prompt_sh~",
                        column: x => x.system_prompt_sha256,
                        principalTable: "analysis_system_prompts",
                        principalColumn: "text_sha256",
                        onDelete: ReferentialAction.Restrict);
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
                name: "vote_revisions",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    author_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    code_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    text_origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    suggestion_headline = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    suggestion_summary = table.Column<string>(type: "text", nullable: true),
                    suggestion_run_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    suggestion_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    vote_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    chamber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    congress = table.Column<int>(type: "integer", nullable: false),
                    session_number = table.Column<int>(type: "integer", nullable: false),
                    roll_call_number = table.Column<int>(type: "integer", nullable: false),
                    bill_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    bill_number = table.Column<int>(type: "integer", nullable: true),
                    candidate_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    candidate_update_utc_ticks = table.Column<long>(type: "bigint", nullable: false),
                    voted_on = table.Column<DateOnly>(type: "date", nullable: false),
                    voted_on_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    voted_on_citation_start = table.Column<int>(type: "integer", nullable: false),
                    voted_on_citation_length = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    kind_citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kind_citation_start = table.Column<int>(type: "integer", nullable: true),
                    kind_citation_length = table.Column<int>(type: "integer", nullable: true),
                    member_list_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    issue_revision_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subjects_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    issue_candidates_recorded = table.Column<bool>(type: "boolean", nullable: false),
                    question = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    result = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    columns_written = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    party_tally_count = table.Column<short>(type: "smallint", nullable: true),
                    alignment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    democrat_majority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    republican_majority = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_revisions", x => new { x.record_id, x.revision_number });
                    table.CheckConstraint("ck_vote_revisions_alignment", "alignment IS NULL OR alignment IN ('Split', 'PartiesAgree', 'Mixed')");
                    table.CheckConstraint("ck_vote_revisions_author_kind", "author_kind IN ('Person','Analysis','Code')");
                    table.CheckConstraint("ck_vote_revisions_bill", "(bill_type IS NULL) = (bill_number IS NULL) AND bill_number >= 1");
                    table.CheckConstraint("ck_vote_revisions_candidate_update_utc_ticks", "candidate_update_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_revisions_chamber", "chamber IN ('House', 'Senate')");
                    table.CheckConstraint("ck_vote_revisions_chamber_source", "(chamber = 'House') = (vote_source = 'CongressGov')");
                    table.CheckConstraint("ck_vote_revisions_code_version", "code_version ~ '^[0-9a-f]{40}$'");
                    table.CheckConstraint("ck_vote_revisions_code_version_author", "code_version IS NULL OR author_kind = 'Code'");
                    table.CheckConstraint("ck_vote_revisions_columns_written", "columns_written OR (alignment IS NULL AND democrat_majority IS NULL AND republican_majority IS NULL)");
                    table.CheckConstraint("ck_vote_revisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_revisions_headline", "length(btrim(headline)) BETWEEN 1 AND 256");
                    table.CheckConstraint("ck_vote_revisions_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_revisions_kind", "kind IN ('Rule', 'PreviousQuestion', 'MotionToRecommit', 'Amendment', 'Suspension', 'Passage', 'ConferenceReport', 'VetoOverride', 'Nomination', 'Cloture', 'MotionToProceed', 'Other')");
                    table.CheckConstraint("ck_vote_revisions_kind_citation", "(kind_citation_extraction_id IS NULL) = (kind_citation_start IS NULL) AND (kind_citation_extraction_id IS NULL) = (kind_citation_length IS NULL) AND kind_citation_start >= 0 AND kind_citation_length > 0");
                    table.CheckConstraint("ck_vote_revisions_majorities", "(democrat_majority IS NULL OR democrat_majority IN ('Yea', 'Nay')) AND (republican_majority IS NULL OR republican_majority IN ('Yea', 'Nay'))");
                    table.CheckConstraint("ck_vote_revisions_number", "revision_number >= 1");
                    table.CheckConstraint("ck_vote_revisions_party_tally_count", "party_tally_count IS NULL OR party_tally_count BETWEEN 0 AND 16");
                    table.CheckConstraint("ck_vote_revisions_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
                    table.CheckConstraint("ck_vote_revisions_run", "(author_kind <> 'Person') = (run_id IS NOT NULL)");
                    table.CheckConstraint("ck_vote_revisions_subjects_hash", "subjects_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_vote_revisions_suggestion", "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND (suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");
                    table.CheckConstraint("ck_vote_revisions_summary", "length(summary) BETWEEN 1 AND 4000");
                    table.CheckConstraint("ck_vote_revisions_text_origin", "text_origin IN ('Model', 'Person', 'Code')");
                    table.CheckConstraint("ck_vote_revisions_vote_source", "vote_source IN ('CongressGov', 'Voteview')");
                    table.CheckConstraint("ck_vote_revisions_voted_on_citation", "voted_on_citation_start >= 0 AND voted_on_citation_length > 0");
                    table.ForeignKey(
                        name: "FK_vote_revisions_document_extractions_kind_citation_extractio~",
                        column: x => x.kind_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_document_extractions_voted_on_citation_extra~",
                        column: x => x.voted_on_citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_analysis_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "vote_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_analysis_runs_suggestion_run_id",
                        column: x => x.suggestion_run_id,
                        principalTable: "vote_analysis_runs",
                        principalColumn: "run_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "vote_candidates",
                        principalColumn: "candidate_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_drafts_record_id",
                        column: x => x.record_id,
                        principalTable: "vote_drafts",
                        principalColumn: "record_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_drafts_record_id_chamber_congress_sessi~",
                        columns: x => new { x.record_id, x.chamber, x.congress, x.session_number, x.roll_call_number },
                        principalTable: "vote_drafts",
                        principalColumns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revisions_vote_member_lists_member_list_id",
                        column: x => x.member_list_id,
                        principalTable: "vote_member_lists",
                        principalColumn: "list_id",
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
                    kind = table.Column<string>(type: "text", nullable: false),
                    actor_subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    correction_note = table.Column<string>(type: "text", nullable: true),
                    reason_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_review_decisions", x => x.decision_id);
                    table.UniqueConstraint("AK_vote_review_decisions_decision_id_record_id", x => new { x.decision_id, x.record_id });
                    table.CheckConstraint("ck_vote_review_decisions_created_at_utc_ticks", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
                    table.CheckConstraint("ck_vote_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_vote_review_decisions_kind", "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");
                    table.CheckConstraint("ck_vote_review_decisions_reason_code", "reason_code IN ('InaccurateSummary', 'Duplicate')");
                    table.CheckConstraint("ck_vote_review_decisions_reason_code_kind", "(kind = 'Reject') = (reason_code IS NOT NULL)");
                    table.CheckConstraint("ck_vote_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
                    table.ForeignKey(
                        name: "FK_vote_review_decisions_vote_revisions_record_id_revision_num~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_revision_facts",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    fact_text = table.Column<string>(type: "text", nullable: false),
                    citation_extraction_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    citation_start = table.Column<int>(type: "integer", nullable: false),
                    citation_length = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_revision_facts", x => new { x.record_id, x.revision_number, x.position });
                    table.CheckConstraint("ck_vote_revision_facts_citation", "citation_start >= 0 AND citation_length > 0");
                    table.CheckConstraint("ck_vote_revision_facts_position", "position >= 0");
                    table.CheckConstraint("ck_vote_revision_facts_text", "length(fact_text) BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_vote_revision_facts_document_extractions_citation_extractio~",
                        column: x => x.citation_extraction_id,
                        principalTable: "document_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_revision_facts_vote_revisions_record_id_revision_number",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_revision_issues",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    issue_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    is_tracked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_revision_issues", x => new { x.record_id, x.revision_number, x.issue_id });
                    table.CheckConstraint("ck_vote_revision_issues_issue", "length(btrim(issue_id)) > 0");
                    table.CheckConstraint("ck_vote_revision_issues_position", "position >= 0");
                    table.ForeignKey(
                        name: "FK_vote_revision_issues_vote_revisions_record_id_revision_numb~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_revision_party_tallies",
                columns: table => new
                {
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision_number = table.Column<int>(type: "integer", nullable: false),
                    party = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    yea = table.Column<short>(type: "smallint", nullable: false),
                    nay = table.Column<short>(type: "smallint", nullable: false),
                    present = table.Column<short>(type: "smallint", nullable: false),
                    not_voting = table.Column<short>(type: "smallint", nullable: false),
                    other = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_revision_party_tallies", x => new { x.record_id, x.revision_number, x.party });
                    table.CheckConstraint("ck_vote_revision_party_tallies_buckets", "yea >= 0 AND nay >= 0 AND present >= 0 AND not_voting >= 0 AND other >= 0");
                    table.CheckConstraint("ck_vote_revision_party_tallies_party", "length(btrim(party)) > 0 AND length(party) <= 256");
                    table.CheckConstraint("ck_vote_revision_party_tallies_total", "yea + nay + present + not_voting + other BETWEEN 1 AND 600");
                    table.ForeignKey(
                        name: "FK_vote_revision_party_tallies_vote_revisions_record_id_revisi~",
                        columns: x => new { x.record_id, x.revision_number },
                        principalTable: "vote_revisions",
                        principalColumns: new[] { "record_id", "revision_number" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vote_review_decision_resolutions",
                columns: table => new
                {
                    decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_decision_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vote_review_decision_resolutions", x => new { x.decision_id, x.resolved_decision_id });
                    table.ForeignKey(
                        name: "FK_vote_review_decision_resolutions_vote_review_decisions_deci~",
                        columns: x => new { x.decision_id, x.record_id },
                        principalTable: "vote_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vote_review_decision_resolutions_vote_review_decisions_reso~",
                        columns: x => new { x.resolved_decision_id, x.record_id },
                        principalTable: "vote_review_decisions",
                        principalColumns: new[] { "decision_id", "record_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_action_analysis_active_claim",
                table: "action_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_retry_of_run_id",
                table: "action_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_status_created_at_utc_ticks_candidate_id",
                table: "action_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_candidate_id_task_started_at_utc_ticks",
                table: "action_analysis_runs",
                columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_output_schema_sha256",
                table: "action_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_previous_run_id",
                table: "action_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_runs_system_prompt_sha256",
                table: "action_analysis_runs",
                column: "system_prompt_sha256");

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
                columns: new[] { "detail_job_id", "includes_summaries", "includes_subjects" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_drafts_candidate_id",
                table: "action_drafts",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_issue_ids",
                table: "action_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_official_ids",
                table: "action_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_action_drafts_refused_candidate_id",
                table: "action_drafts",
                column: "refused_candidate_id");

            migrationBuilder.CreateIndex(
                name: "ux_action_drafts_bill",
                table: "action_drafts",
                columns: new[] { "source_id", "congress", "bill_type", "bill_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decision_resolutions_decision_id_record_id",
                table: "action_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_action_review_decision_resolutions_resolved_decision_id_rec~",
                table: "action_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

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
                name: "IX_action_revision_key_terms_citation_extraction_id",
                table: "action_revision_key_terms",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revision_sponsorship_citations_citation_extraction_id",
                table: "action_revision_sponsorship_citations",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_revision_sponsorships_person",
                table: "action_revision_sponsorships",
                columns: new[] { "person_id", "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_action_revision_text_citations_citation_extraction_id",
                table: "action_revision_text_citations",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_bill_citation_extraction_id",
                table: "action_revisions",
                column: "bill_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_action_revisions_bill_identity",
                table: "action_revisions",
                columns: new[] { "congress", "bill_number", "bill_type", "record_id", "revision_number" },
                filter: "headline IS NOT NULL AND event_on IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_candidate_id",
                table: "action_revisions",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_congress_bill_type_bill_number_text_packag~",
                table: "action_revisions",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" });

            migrationBuilder.CreateIndex(
                name: "ix_action_revisions_event_on",
                table: "action_revisions",
                column: "event_on");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_policy_area_citation_extraction_id",
                table: "action_revisions",
                column: "policy_area_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_record_id_source_id_congress_bill_type_bil~",
                table: "action_revisions",
                columns: new[] { "record_id", "source_id", "congress", "bill_type", "bill_number" });

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_run_id",
                table: "action_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_suggestion_run_id",
                table: "action_revisions",
                column: "suggestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_action_revisions_summary_citation_extraction_id",
                table: "action_revisions",
                column: "summary_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_bill_actions_job_id",
                table: "bill_actions",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_bill_follow_on_jobs_detail_job_id",
                table: "bill_follow_on_jobs",
                column: "detail_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_bill_gates_congress_bill_type_bill_number_text_package_id",
                table: "bill_gates",
                columns: new[] { "congress", "bill_type", "bill_number", "text_package_id" });

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
                name: "IX_bill_text_versions_extraction_id",
                table: "bill_text_versions",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_brief_analysis_active_claim",
                table: "brief_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "record_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_retry_of_run_id",
                table: "brief_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_status_created_at_utc_ticks_record_id",
                table: "brief_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "record_id" });

            migrationBuilder.CreateIndex(
                name: "ux_brief_analysis_week",
                table: "brief_analysis",
                columns: new[] { "week_start", "task" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_output_schema_sha256",
                table: "brief_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_previous_run_id",
                table: "brief_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_record_id_checked_revision_number",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "checked_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_record_id_task_started_at_utc_ticks",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_source_run_id",
                table: "brief_analysis_runs",
                column: "source_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_runs_system_prompt_sha256",
                table: "brief_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ux_brief_drafts_week",
                table: "brief_drafts",
                column: "week_start",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decision_resolutions_decision_id_record_id",
                table: "brief_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_review_decision_resolutions_resolved_decision_id_reco~",
                table: "brief_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

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
                name: "IX_brief_revision_citations_action_record_id_action_revision_n~",
                table: "brief_revision_citations",
                columns: new[] { "action_record_id", "action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_document_change_draft_id_document_~",
                table: "brief_revision_citations",
                columns: new[] { "document_change_draft_id", "document_change_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_act~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "action_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_doc~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "document_change_draft_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_record_id_revision_number_part_sta~",
                table: "brief_revision_citations",
                columns: new[] { "record_id", "revision_number", "part", "statement_candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_citations_statement_candidate_id_statement_r~",
                table: "brief_revision_citations",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_paragraph_citations_record_id_revision_numb~1",
                table: "brief_revision_paragraph_citations",
                columns: new[] { "record_id", "revision_number", "part", "paragraph_position", "citation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revision_paragraph_citations_record_id_revision_numbe~",
                table: "brief_revision_paragraph_citations",
                columns: new[] { "record_id", "revision_number", "part", "citation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_record_id_week_start",
                table: "brief_revisions",
                columns: new[] { "record_id", "week_start" });

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_run_id",
                table: "brief_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_brief_revisions_suggestion_run_id",
                table: "brief_revisions",
                column: "suggestion_run_id");

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
                filter: "completed_at IS NULL");

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
                name: "ix_collection_jobs_active_claim",
                table: "collection_jobs",
                columns: new[] { "priority", "created_at", "job_id" },
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
                name: "IX_document_change_analysis_retry_of_run_id",
                table: "document_change_analysis",
                column: "retry_of_run_id");

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
                name: "IX_document_change_analysis_runs_output_schema_sha256",
                table: "document_change_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_analysis_runs_previous_run_id",
                table: "document_change_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_analysis_runs_system_prompt_sha256",
                table: "document_change_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_drafts_comparison_id",
                table: "document_change_drafts",
                column: "comparison_id");

            migrationBuilder.CreateIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "draft_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decision_resolutions_decision_id_dra~",
                table: "document_change_review_decision_resolutions",
                columns: new[] { "decision_id", "draft_id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_change_review_decision_resolutions_resolved_decisi~",
                table: "document_change_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "draft_id" });

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
                name: "IX_document_change_revision_citations_citation_extraction_id",
                table: "document_change_revision_citations",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revision_citations_draft_id_revision_number~",
                table: "document_change_revision_citations",
                columns: new[] { "draft_id", "revision_number", "citation_extraction_id", "citation_start", "citation_length" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_document_change_revision_citations_change_date_evidence",
                table: "document_change_revision_citations",
                columns: new[] { "draft_id", "revision_number" },
                unique: true,
                filter: "is_change_date_evidence");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_revisions_draft_id_comparison_id",
                table: "document_change_revisions",
                columns: new[] { "draft_id", "comparison_id" });

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
                name: "IX_evidence_processing_comparison_id",
                table: "evidence_processing",
                column: "comparison_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_extraction_id",
                table: "evidence_processing",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_job_id_stage_status",
                table: "evidence_processing",
                columns: new[] { "job_id", "stage", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_status_observed_at_utc_ticks_attempt_id~",
                table: "evidence_processing",
                columns: new[] { "status", "observed_at_utc_ticks", "attempt_id", "job_id" });

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
                name: "IX_executive_action_documents_document_job_id",
                table: "executive_action_documents",
                column: "document_job_id");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_documents_notice_citation_extraction_id",
                table: "executive_action_documents",
                column: "notice_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_documents_published",
                table: "executive_action_documents",
                columns: new[] { "source_id", "published_on" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_documents_record_id_revision_number",
                table: "executive_action_documents",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_issue_ids",
                table: "executive_action_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_official_ids",
                table: "executive_action_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decision_resolutions_decision_id_re~",
                table: "executive_action_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decision_resolutions_resolved_decis~",
                table: "executive_action_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decisions_record_id_review_state_ve~",
                table: "executive_action_review_decisions",
                columns: new[] { "record_id", "review_state_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_review_decisions_record_id_revision_number",
                table: "executive_action_review_decisions",
                columns: new[] { "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_disposition_targets_record_id_rev~",
                table: "executive_action_revision_disposition_targets",
                columns: new[] { "record_id", "revision_number", "relation", "kind", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_facts_citation_extraction_id",
                table: "executive_action_revision_facts",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_notices_record_id_revision_number~",
                table: "executive_action_revision_notices",
                columns: new[] { "record_id", "revision_number", "document_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_notices_source_id_document_number",
                table: "executive_action_revision_notices",
                columns: new[] { "source_id", "document_number" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revision_summary_sentences_quote_extractio~",
                table: "executive_action_revision_summary_sentences",
                column: "quote_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revisions_disposition_notes_extraction_id",
                table: "executive_action_revisions",
                column: "disposition_notes_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revisions_kind_presidential_number",
                table: "executive_action_revisions",
                columns: new[] { "kind", "presidential_number" });

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_revisions_run_id",
                table: "executive_action_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executive_action_texts_extraction_id",
                table: "executive_action_texts",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_official_photos_person_id",
                table: "official_photos",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "IX_people_bioguide_id",
                table: "people",
                column: "bioguide_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_people_created_by_attempt_id",
                table: "people",
                column: "created_by_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_people_govtrack_id",
                table: "people",
                column: "govtrack_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_people_wikidata_id",
                table: "people",
                column: "wikidata_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_person_office_revisions_registry_sequence",
                table: "person_office_revisions",
                column: "registry_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_person_setting_revisions_registry_sequence",
                table: "person_setting_revisions",
                column: "registry_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_record_states_first_release",
                table: "publication_record_states",
                column: "first_release");

            migrationBuilder.CreateIndex(
                name: "IX_publication_record_states_last_release",
                table: "publication_record_states",
                column: "last_release");

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
                name: "IX_publication_withdrawals_first_release",
                table: "publication_withdrawals",
                column: "first_release");

            migrationBuilder.CreateIndex(
                name: "IX_publication_withdrawals_last_release",
                table: "publication_withdrawals",
                column: "last_release");

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_action_record_id_action_revision_number",
                table: "published_entries",
                columns: new[] { "action_record_id", "action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_brief_record_id_brief_revision_number",
                table: "published_entries",
                columns: new[] { "brief_record_id", "brief_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_document_change_draft_id_document_change_~",
                table: "published_entries",
                columns: new[] { "document_change_draft_id", "document_change_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_executive_action_record_id_executive_acti~",
                table: "published_entries",
                columns: new[] { "executive_action_record_id", "executive_action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_relationship_record_id_relationship_revis~",
                table: "published_entries",
                columns: new[] { "relationship_record_id", "relationship_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_statement_candidate_id_statement_revision~",
                table: "published_entries",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_statement_kind_statement_record_id",
                table: "published_entries",
                columns: new[] { "statement_kind", "statement_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_target_kind_target_record_id",
                table: "published_entries",
                columns: new[] { "target_kind", "target_record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_published_entries_vote_record_id_vote_revision_number",
                table: "published_entries",
                columns: new[] { "vote_record_id", "vote_revision_number" });

            migrationBuilder.CreateIndex(
                name: "ix_published_entry_officials_official_id",
                table: "published_entry_officials",
                column: "official_id");

            migrationBuilder.CreateIndex(
                name: "ux_published_entry_officials_matched",
                table: "published_entry_officials",
                columns: new[] { "kind", "record_id", "revision_number" },
                unique: true,
                filter: "is_matched");

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
                name: "IX_relationship_analysis_retry_of_run_id",
                table: "relationship_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_statement_candidate_id",
                table: "relationship_analysis",
                column: "statement_candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_status_created_at_utc_ticks_pair_id",
                table: "relationship_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "pair_id" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_output_schema_sha256",
                table: "relationship_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_pair_id_task_started_at_utc_ticks",
                table: "relationship_analysis_runs",
                columns: new[] { "pair_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_previous_run_id",
                table: "relationship_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_runs_system_prompt_sha256",
                table: "relationship_analysis_runs",
                column: "system_prompt_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_review_decision_resolutions_decision_id_record~",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_review_decision_resolutions_resolved_decision_~",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

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
                name: "IX_relationship_revisions_match_extraction_id",
                table: "relationship_revisions",
                column: "match_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_run_id",
                table: "relationship_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_statement_candidate_id_statement_rev~",
                table: "relationship_revisions",
                columns: new[] { "statement_candidate_id", "statement_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_statement_quote_extraction_id",
                table: "relationship_revisions",
                column: "statement_quote_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_target_action_record_id_target_actio~",
                table: "relationship_revisions",
                columns: new[] { "target_action_record_id", "target_action_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_relationship_revisions_target_vote_record_id_target_vote_re~",
                table: "relationship_revisions",
                columns: new[] { "target_vote_record_id", "target_vote_revision_number" });

            migrationBuilder.CreateIndex(
                name: "IX_release_bill_histories_detail_job_id_projection_version",
                table: "release_bill_histories",
                columns: new[] { "detail_job_id", "projection_version" });

            migrationBuilder.CreateIndex(
                name: "IX_release_correction_histories_history_id",
                table: "release_correction_histories",
                column: "history_id");

            migrationBuilder.CreateIndex(
                name: "IX_release_correction_histories_kind_record_id_revision_number",
                table: "release_correction_histories",
                columns: new[] { "kind", "record_id", "revision_number" });

            migrationBuilder.CreateIndex(
                name: "ix_release_official_bill_counts_release_official",
                table: "release_official_bill_counts",
                columns: new[] { "release_number", "official_id" });

            migrationBuilder.CreateIndex(
                name: "ix_statement_analysis_active_claim",
                table: "statement_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_retry_of_run_id",
                table: "statement_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_status_created_at_utc_ticks_candidate_id",
                table: "statement_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_candidate_id_task_started_at_utc_ti~",
                table: "statement_analysis_runs",
                columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_output_schema_sha256",
                table: "statement_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_previous_run_id",
                table: "statement_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_runs_system_prompt_sha256",
                table: "statement_analysis_runs",
                column: "system_prompt_sha256");

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
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "candidate_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

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
                name: "IX_statement_gate_labels_extraction_id",
                table: "statement_gate_labels",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_gate_labels_run_id",
                table: "statement_gate_labels",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decision_resolutions_decision_id_candidate~",
                table: "statement_review_decision_resolutions",
                columns: new[] { "decision_id", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_review_decision_resolutions_resolved_decision_id_~",
                table: "statement_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "candidate_id" });

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
                name: "IX_statement_revision_bill_numbers_bill_number",
                table: "statement_revision_bill_numbers",
                column: "bill_number");

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_other_speakers_candidate_id_revision_num~",
                table: "statement_revision_other_speakers",
                columns: new[] { "candidate_id", "revision_number", "speaker_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_candidate_id_quotation_extrac~",
                table: "statement_revision_quotations",
                columns: new[] { "candidate_id", "quotation_extraction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_candidate_id_revision_number_~",
                table: "statement_revision_quotations",
                columns: new[] { "candidate_id", "revision_number", "quotation_extraction_id", "quotation_start", "quotation_length" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_revision_quotations_quotation_extraction_id",
                table: "statement_revision_quotations",
                column: "quotation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_candidate_id_statement_date_evidence_ex~",
                table: "statement_revisions",
                columns: new[] { "candidate_id", "statement_date_evidence_extraction_id" });

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_run_id",
                table: "statement_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_statement_revisions_statement_date_evidence_extraction_id",
                table: "statement_revisions",
                column: "statement_date_evidence_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_analysis_active_claim",
                table: "vote_analysis",
                columns: new[] { "task", "priority", "created_at_utc_ticks", "candidate_id" },
                filter: "status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_retry_of_run_id",
                table: "vote_analysis",
                column: "retry_of_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_status_created_at_utc_ticks_candidate_id",
                table: "vote_analysis",
                columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_candidate_id_task_started_at_utc_ticks",
                table: "vote_analysis_runs",
                columns: new[] { "candidate_id", "task", "started_at_utc_ticks" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_output_schema_sha256",
                table: "vote_analysis_runs",
                column: "output_schema_sha256");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_previous_run_id",
                table: "vote_analysis_runs",
                column: "previous_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_runs_system_prompt_sha256",
                table: "vote_analysis_runs",
                column: "system_prompt_sha256");

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
                name: "IX_vote_candidates_roster_job_id",
                table: "vote_candidates",
                column: "roster_job_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_candidates_detail_job",
                table: "vote_candidates",
                columns: new[] { "detail_job_id", "session_number", "roll_call_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_drafts_candidate_id",
                table: "vote_drafts",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_issue_ids",
                table: "vote_drafts",
                column: "issue_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_official_ids",
                table: "vote_drafts",
                column: "official_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_vote_drafts_refused_candidate_id",
                table: "vote_drafts",
                column: "refused_candidate_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_drafts_roll",
                table: "vote_drafts",
                columns: new[] { "source_id", "chamber", "congress", "session_number", "roll_call_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_member_list_entries_citation_extraction_id",
                table: "vote_member_list_entries",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "ix_vote_member_list_entries_members",
                table: "vote_member_list_entries",
                column: "list_id")
                .Annotation("Npgsql:IndexInclude", new[] { "person_id", "party", "vote_cast" });

            migrationBuilder.CreateIndex(
                name: "ix_vote_member_list_entries_person",
                table: "vote_member_list_entries",
                columns: new[] { "person_id", "list_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decision_resolutions_decision_id_record_id",
                table: "vote_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_review_decision_resolutions_resolved_decision_id_recor~",
                table: "vote_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" });

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
                name: "IX_vote_revision_facts_citation_extraction_id",
                table: "vote_revision_facts",
                column: "citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_candidate_id",
                table: "vote_revisions",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_kind_citation_extraction_id",
                table: "vote_revisions",
                column: "kind_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_member_list_id",
                table: "vote_revisions",
                column: "member_list_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_record_id_chamber_congress_session_number_ro~",
                table: "vote_revisions",
                columns: new[] { "record_id", "chamber", "congress", "session_number", "roll_call_number" });

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_run_id",
                table: "vote_revisions",
                column: "run_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_suggestion_run_id",
                table: "vote_revisions",
                column: "suggestion_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_revisions_voted_on_citation_extraction_id",
                table: "vote_revisions",
                column: "voted_on_citation_extraction_id");

            migrationBuilder.CreateIndex(
                name: "IX_vote_versions_extraction_id",
                table: "vote_versions",
                column: "extraction_id");

            migrationBuilder.CreateIndex(
                name: "ux_vote_versions_roll",
                table: "vote_versions",
                columns: new[] { "source_id", "chamber", "congress", "session_number", "roll_call_number", "update_date_utc_ticks" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_action_analysis_action_analysis_runs_retry_of_run_id",
                table: "action_analysis",
                column: "retry_of_run_id",
                principalTable: "action_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_brief_analysis_runs_retry_of_run_id",
                table: "brief_analysis",
                column: "retry_of_run_id",
                principalTable: "brief_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_runs_brief_revisions_record_id_checked_revis~",
                table: "brief_analysis_runs",
                columns: new[] { "record_id", "checked_revision_number" },
                principalTable: "brief_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_revision_citations_statement_revisions_statement_cand~",
                table: "brief_revision_citations",
                columns: new[] { "statement_candidate_id", "statement_revision_number" },
                principalTable: "statement_revisions",
                principalColumns: new[] { "candidate_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_analysis_document_change_analysis_runs_retr~",
                table: "document_change_analysis",
                column: "retry_of_run_id",
                principalTable: "document_change_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_exec_summary_analysis_exec_summary_analysis_runs_retry_of_r~",
                table: "exec_summary_analysis",
                column: "retry_of_run_id",
                principalTable: "exec_summary_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_entries_relationship_revisions_relationship_recor~",
                table: "published_entries",
                columns: new[] { "relationship_record_id", "relationship_revision_number" },
                principalTable: "relationship_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_entries_statement_revisions_statement_candidate_i~",
                table: "published_entries",
                columns: new[] { "statement_candidate_id", "statement_revision_number" },
                principalTable: "statement_revisions",
                principalColumns: new[] { "candidate_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_published_entries_vote_revisions_vote_record_id_vote_revisi~",
                table: "published_entries",
                columns: new[] { "vote_record_id", "vote_revision_number" },
                principalTable: "vote_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_analysis_relationship_analysis_runs_retry_of_r~",
                table: "relationship_analysis",
                column: "retry_of_run_id",
                principalTable: "relationship_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_review_decision_resolutions_relationship_revie~",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "decision_id", "record_id" },
                principalTable: "relationship_review_decisions",
                principalColumns: new[] { "decision_id", "record_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_review_decision_resolutions_relationship_revi~1",
                table: "relationship_review_decision_resolutions",
                columns: new[] { "resolved_decision_id", "record_id" },
                principalTable: "relationship_review_decisions",
                principalColumns: new[] { "decision_id", "record_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_review_decisions_relationship_revisions_record~",
                table: "relationship_review_decisions",
                columns: new[] { "record_id", "revision_number" },
                principalTable: "relationship_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_statement_revisions_statement_candid~",
                table: "relationship_revisions",
                columns: new[] { "statement_candidate_id", "statement_revision_number" },
                principalTable: "statement_revisions",
                principalColumns: new[] { "candidate_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_revisions_vote_revisions_target_vote_record_id~",
                table: "relationship_revisions",
                columns: new[] { "target_vote_record_id", "target_vote_revision_number" },
                principalTable: "vote_revisions",
                principalColumns: new[] { "record_id", "revision_number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_analysis_statement_analysis_runs_retry_of_run_id",
                table: "statement_analysis",
                column: "retry_of_run_id",
                principalTable: "statement_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_analysis_vote_analysis_runs_retry_of_run_id",
                table: "vote_analysis",
                column: "retry_of_run_id",
                principalTable: "vote_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            CreateHandWrittenObjects(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropHandWrittenObjects(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "FK_action_analysis_action_analysis_runs_retry_of_run_id",
                table: "action_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_runs_analysis_output_schemas_output_schema_s~",
                table: "brief_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_runs_analysis_output_schemas_outpu~",
                table: "document_change_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_exec_summary_analysis_runs_analysis_output_schemas_output_s~",
                table: "exec_summary_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_runs_analysis_output_schemas_output_s~",
                table: "relationship_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_runs_analysis_output_schemas_output_sche~",
                table: "statement_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_runs_analysis_output_schemas_output_schema_sh~",
                table: "vote_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_runs_analysis_system_prompts_system_prompt_s~",
                table: "brief_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_runs_analysis_system_prompts_syste~",
                table: "document_change_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_exec_summary_analysis_runs_analysis_system_prompts_system_p~",
                table: "exec_summary_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_runs_analysis_system_prompts_system_p~",
                table: "relationship_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_runs_analysis_system_prompts_system_prom~",
                table: "statement_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_runs_analysis_system_prompts_system_prompt_sh~",
                table: "vote_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "fk_collection_jobs_configuration_revision",
                table: "collection_jobs");

            migrationBuilder.DropForeignKey(
                name: "fk_vote_candidates_configuration_revision",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_document_comparisons_document_extractions_after_extraction_~",
                table: "document_comparisons");

            migrationBuilder.DropForeignKey(
                name: "FK_document_comparisons_document_extractions_before_extraction~",
                table: "document_comparisons");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_candidates_document_extractions_extraction_id",
                table: "statement_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_document_extractions_detail_extraction_id",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_document_extractions_members_extraction_id",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_versions_document_extractions_extraction_id",
                table: "vote_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_collection_jobs_members_job_id",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_candidates_collection_jobs_roster_job_id",
                table: "vote_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_versions_collection_jobs_detail_job_id",
                table: "vote_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_brief_analysis_runs_retry_of_run_id",
                table: "brief_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_revisions_brief_analysis_runs_run_id",
                table: "brief_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_revisions_brief_analysis_runs_suggestion_run_id",
                table: "brief_revisions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_runs_document_change_revisions_dra~",
                table: "document_change_analysis_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_collection_attempts_collection_captures_capture_sha256",
                table: "collection_attempts");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_candidates_collection_attempts_attempt_id",
                table: "statement_candidates");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_document_change_analysis_runs_retr~",
                table: "document_change_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_exec_summary_analysis_exec_summary_analysis_runs_retry_of_r~",
                table: "exec_summary_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_relationship_analysis_runs_retry_of_r~",
                table: "relationship_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_statement_candidates_candidate_id",
                table: "statement_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_statement_analysis_runs_retry_of_run_id",
                table: "statement_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_vote_analysis_runs_retry_of_run_id",
                table: "vote_analysis");

            migrationBuilder.DropTable(
                name: "action_analysis_budget");

            migrationBuilder.DropTable(
                name: "action_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "action_revision_issues");

            migrationBuilder.DropTable(
                name: "action_revision_key_terms");

            migrationBuilder.DropTable(
                name: "action_revision_sponsorship_citations");

            migrationBuilder.DropTable(
                name: "action_revision_text_citations");

            migrationBuilder.DropTable(
                name: "bill_action_recorded_votes");

            migrationBuilder.DropTable(
                name: "bill_follow_on_jobs");

            migrationBuilder.DropTable(
                name: "bill_stream_cursors");

            migrationBuilder.DropTable(
                name: "bill_stream_pages");

            migrationBuilder.DropTable(
                name: "brief_analysis_budget");

            migrationBuilder.DropTable(
                name: "brief_check_flags");

            migrationBuilder.DropTable(
                name: "brief_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "brief_revision_paragraph_citations");

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
                name: "document_change_analysis_status");

            migrationBuilder.DropTable(
                name: "document_change_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "document_change_review_idempotency");

            migrationBuilder.DropTable(
                name: "document_change_revision_citations");

            migrationBuilder.DropTable(
                name: "document_change_revision_issues");

            migrationBuilder.DropTable(
                name: "document_change_revision_officials");

            migrationBuilder.DropTable(
                name: "document_comparison_word_edits");

            migrationBuilder.DropTable(
                name: "evidence_processing");

            migrationBuilder.DropTable(
                name: "executive_action_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "executive_action_revision_disposition_targets");

            migrationBuilder.DropTable(
                name: "executive_action_revision_facts");

            migrationBuilder.DropTable(
                name: "executive_action_revision_notices");

            migrationBuilder.DropTable(
                name: "executive_action_revision_summary_sentences");

            migrationBuilder.DropTable(
                name: "executive_action_texts");

            migrationBuilder.DropTable(
                name: "executive_terms");

            migrationBuilder.DropTable(
                name: "official_photos");

            migrationBuilder.DropTable(
                name: "person_office_revision_terms");

            migrationBuilder.DropTable(
                name: "person_setting_revisions");

            migrationBuilder.DropTable(
                name: "publication_record_states");

            migrationBuilder.DropTable(
                name: "publication_withdrawal_corrections");

            migrationBuilder.DropTable(
                name: "published_bill_history_actions");

            migrationBuilder.DropTable(
                name: "published_correction_history_entries");

            migrationBuilder.DropTable(
                name: "published_entry_issues");

            migrationBuilder.DropTable(
                name: "published_entry_officials");

            migrationBuilder.DropTable(
                name: "published_entry_policy_areas");

            migrationBuilder.DropTable(
                name: "reader_report_cursor");

            migrationBuilder.DropTable(
                name: "reader_reports");

            migrationBuilder.DropTable(
                name: "relationship_analysis_budget");

            migrationBuilder.DropTable(
                name: "relationship_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "release_bill_histories");

            migrationBuilder.DropTable(
                name: "release_bill_statuses");

            migrationBuilder.DropTable(
                name: "release_correction_histories");

            migrationBuilder.DropTable(
                name: "release_issue_excludes");

            migrationBuilder.DropTable(
                name: "release_issue_includes");

            migrationBuilder.DropTable(
                name: "release_issue_party_counts");

            migrationBuilder.DropTable(
                name: "release_official_bill_counts");

            migrationBuilder.DropTable(
                name: "release_official_record_counts");

            migrationBuilder.DropTable(
                name: "release_official_terms");

            migrationBuilder.DropTable(
                name: "release_policy_areas");

            migrationBuilder.DropTable(
                name: "release_vote_bills");

            migrationBuilder.DropTable(
                name: "roster_terms");

            migrationBuilder.DropTable(
                name: "statement_analysis_budget");

            migrationBuilder.DropTable(
                name: "statement_gate_labels");

            migrationBuilder.DropTable(
                name: "statement_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "statement_revision_bill_numbers");

            migrationBuilder.DropTable(
                name: "statement_revision_issues");

            migrationBuilder.DropTable(
                name: "statement_revision_other_speakers");

            migrationBuilder.DropTable(
                name: "statement_revision_policy_areas");

            migrationBuilder.DropTable(
                name: "statement_revision_quotations");

            migrationBuilder.DropTable(
                name: "statement_revision_speakers");

            migrationBuilder.DropTable(
                name: "vote_analysis_budget");

            migrationBuilder.DropTable(
                name: "vote_member_list_entries");

            migrationBuilder.DropTable(
                name: "vote_review_decision_resolutions");

            migrationBuilder.DropTable(
                name: "vote_revision_facts");

            migrationBuilder.DropTable(
                name: "vote_revision_issues");

            migrationBuilder.DropTable(
                name: "vote_revision_party_tallies");

            migrationBuilder.DropTable(
                name: "worker_heartbeat");

            migrationBuilder.DropTable(
                name: "zcta_districts");

            migrationBuilder.DropTable(
                name: "action_review_decisions");

            migrationBuilder.DropTable(
                name: "action_revision_sponsorships");

            migrationBuilder.DropTable(
                name: "bill_actions");

            migrationBuilder.DropTable(
                name: "brief_review_decisions");

            migrationBuilder.DropTable(
                name: "brief_revision_citations");

            migrationBuilder.DropTable(
                name: "brief_revision_paragraphs");

            migrationBuilder.DropTable(
                name: "collection_discoveries");

            migrationBuilder.DropTable(
                name: "collection_job_attempts");

            migrationBuilder.DropTable(
                name: "document_change_review_decisions");

            migrationBuilder.DropTable(
                name: "document_comparison_hunks");

            migrationBuilder.DropTable(
                name: "executive_action_review_decisions");

            migrationBuilder.DropTable(
                name: "executive_action_documents");

            migrationBuilder.DropTable(
                name: "executive_officials");

            migrationBuilder.DropTable(
                name: "person_office_revisions");

            migrationBuilder.DropTable(
                name: "publication_withdrawals");

            migrationBuilder.DropTable(
                name: "relationship_review_decisions");

            migrationBuilder.DropTable(
                name: "published_bill_histories");

            migrationBuilder.DropTable(
                name: "published_correction_histories");

            migrationBuilder.DropTable(
                name: "published_entries");

            migrationBuilder.DropTable(
                name: "release_issue_party_lineups");

            migrationBuilder.DropTable(
                name: "release_bill_counts");

            migrationBuilder.DropTable(
                name: "release_officials");

            migrationBuilder.DropTable(
                name: "roster_members");

            migrationBuilder.DropTable(
                name: "statement_review_decisions");

            migrationBuilder.DropTable(
                name: "vote_review_decisions");

            migrationBuilder.DropTable(
                name: "bill_pages");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "executive_action_revisions");

            migrationBuilder.DropTable(
                name: "published_records");

            migrationBuilder.DropTable(
                name: "relationship_revisions");

            migrationBuilder.DropTable(
                name: "release_issues");

            migrationBuilder.DropTable(
                name: "roster_snapshots");

            migrationBuilder.DropTable(
                name: "action_revisions");

            migrationBuilder.DropTable(
                name: "relationship_drafts");

            migrationBuilder.DropTable(
                name: "statement_revisions");

            migrationBuilder.DropTable(
                name: "vote_revisions");

            migrationBuilder.DropTable(
                name: "publication_releases");

            migrationBuilder.DropTable(
                name: "action_drafts");

            migrationBuilder.DropTable(
                name: "statement_drafts");

            migrationBuilder.DropTable(
                name: "vote_drafts");

            migrationBuilder.DropTable(
                name: "vote_member_lists");

            migrationBuilder.DropTable(
                name: "action_analysis_runs");

            migrationBuilder.DropTable(
                name: "action_analysis");

            migrationBuilder.DropTable(
                name: "action_candidates");

            migrationBuilder.DropTable(
                name: "bill_gates");

            migrationBuilder.DropTable(
                name: "bill_text_versions");

            migrationBuilder.DropTable(
                name: "analysis_output_schemas");

            migrationBuilder.DropTable(
                name: "analysis_system_prompts");

            migrationBuilder.DropTable(
                name: "collection_configuration_revisions");

            migrationBuilder.DropTable(
                name: "document_extractions");

            migrationBuilder.DropTable(
                name: "collection_jobs");

            migrationBuilder.DropTable(
                name: "brief_analysis_runs");

            migrationBuilder.DropTable(
                name: "brief_analysis");

            migrationBuilder.DropTable(
                name: "brief_revisions");

            migrationBuilder.DropTable(
                name: "brief_drafts");

            migrationBuilder.DropTable(
                name: "document_change_revisions");

            migrationBuilder.DropTable(
                name: "collection_captures");

            migrationBuilder.DropTable(
                name: "collection_attempts");

            migrationBuilder.DropTable(
                name: "document_change_analysis_runs");

            migrationBuilder.DropTable(
                name: "document_change_analysis");

            migrationBuilder.DropTable(
                name: "document_change_drafts");

            migrationBuilder.DropTable(
                name: "document_comparisons");

            migrationBuilder.DropTable(
                name: "exec_summary_analysis_runs");

            migrationBuilder.DropTable(
                name: "exec_summary_analysis");

            migrationBuilder.DropTable(
                name: "executive_action_drafts");

            migrationBuilder.DropTable(
                name: "relationship_analysis_runs");

            migrationBuilder.DropTable(
                name: "relationship_analysis");

            migrationBuilder.DropTable(
                name: "statement_candidates");

            migrationBuilder.DropTable(
                name: "statement_analysis_runs");

            migrationBuilder.DropTable(
                name: "statement_analysis");

            migrationBuilder.DropTable(
                name: "vote_analysis_runs");

            migrationBuilder.DropTable(
                name: "vote_analysis");

            migrationBuilder.DropTable(
                name: "vote_candidates");

            migrationBuilder.DropTable(
                name: "vote_versions");

            migrationBuilder.DropSequence(
                name: "person_registry_sequence");

            DropNotifyFunctions(migrationBuilder);
        }
    }
}
