using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012180000_StatementCandidateQueue")]
public partial class StatementCandidateQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                created_at_utc_ticks = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_statement_analysis", x => new { x.candidate_id, x.task });
                table.CheckConstraint("ck_statement_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
                table.CheckConstraint("ck_statement_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                table.CheckConstraint("ck_statement_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
                table.CheckConstraint("ck_statement_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
                table.CheckConstraint("ck_statement_analysis_reservation", "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
                table.CheckConstraint("ck_statement_analysis_status", "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
                table.ForeignKey(
                    name: "FK_statement_analysis_statement_candidates_candidate_id",
                    column: x => x.candidate_id,
                    principalTable: "statement_candidates",
                    principalColumn: "candidate_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_statement_candidates_attempt_id",
            table: "statement_candidates",
            column: "attempt_id");

        migrationBuilder.CreateIndex(
            name: "IX_statement_candidates_extraction_id",
            table: "statement_candidates",
            column: "extraction_id");

        migrationBuilder.CreateIndex(
            name: "IX_statement_analysis_status_created_at_utc_ticks_candidate_id",
            table: "statement_analysis",
            columns: new[] { "status", "created_at_utc_ticks", "candidate_id" });

        migrationBuilder.Sql("""
            INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
            SELECT DISTINCT ON (cj.candidate_hash)
                   cj.candidate_hash, a.source_id, a.requested_url, e.extraction_id, a.attempt_id,
                   (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint
              FROM document_extractions e
              JOIN collection_attempts a ON a.attempt_id = e.attempt_id
              JOIN collection_candidate_jobs cj ON cj.source_id = a.source_id AND cj.url = a.requested_url
             WHERE EXISTS (SELECT 1
                             FROM collection_job_attempts ja
                             JOIN collection_jobs j ON j.job_id = ja.job_id
                            WHERE ja.attempt_id = a.attempt_id AND starts_with(j.idempotency_key, 'discovery-admission/'))
             ORDER BY cj.candidate_hash, a.observed_at_utc_ticks, a.attempt_id, e.extraction_id;

            INSERT INTO statement_analysis (candidate_id, task, status, fence, attempts, reserved_tokens, created_at_utc_ticks)
            SELECT candidate_id, 'gate', 'Pending', 0, 0, 0, created_at_utc_ticks FROM statement_candidates;

            CREATE TRIGGER trg_statement_analysis_notify
            AFTER INSERT OR UPDATE OF status, retry_at ON statement_analysis
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_analysis_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_statement_analysis_notify ON statement_analysis;");
        migrationBuilder.DropTable(name: "statement_analysis");
        migrationBuilder.DropTable(name: "statement_candidates");
    }
}
