using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

public partial class DurableEvidenceProcessing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                table.CheckConstraint("ck_evidence_processing_fence", "fence >= 0 AND attempts >= 0");
                table.CheckConstraint("ck_evidence_processing_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
                table.CheckConstraint("ck_evidence_processing_stage", "stage IN ('Preparation','Extraction','Comparison','Complete')");
                table.CheckConstraint("ck_evidence_processing_status", "status IN ('Pending','Running','RetryWaiting','Succeeded','Blocked','Failed')");
                table.CheckConstraint("ck_evidence_processing_evidence_ids", "(extraction_id IS NULL OR extraction_id ~ '^[0-9a-f]{64}$') AND (comparison_id IS NULL OR comparison_id ~ '^[0-9a-f]{64}$')");
                table.CheckConstraint("ck_evidence_processing_counts", "admitted_count >= 0 AND deferred_count >= 0 AND duplicate_count >= 0");
            });
        migrationBuilder.CreateIndex("IX_evidence_processing_job_id_stage_status", "evidence_processing",
            new[] { "job_id", "stage", "status" });
        migrationBuilder.CreateIndex("IX_evidence_processing_status_observed_at_utc_ticks_attempt_id_job_id", "evidence_processing",
            new[] { "status", "observed_at_utc_ticks", "attempt_id", "job_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("evidence_processing");
}
