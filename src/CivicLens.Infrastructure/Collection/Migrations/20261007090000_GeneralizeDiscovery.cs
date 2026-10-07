using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

/// <inheritdoc />
public partial class GeneralizeDiscovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable("collection_feed_discoveries", newName: "collection_discoveries");
        migrationBuilder.RenameTable("collection_feed_candidate_jobs", newName: "collection_candidate_jobs");
        migrationBuilder.RenameTable("collection_feed_admission_batches", newName: "collection_admission_batches");
        migrationBuilder.Sql("""
            ALTER TABLE collection_discoveries RENAME CONSTRAINT "PK_collection_feed_discoveries" TO "PK_collection_discoveries";
            ALTER TABLE collection_candidate_jobs RENAME CONSTRAINT "PK_collection_feed_candidate_jobs" TO "PK_collection_candidate_jobs";
            ALTER TABLE collection_admission_batches RENAME CONSTRAINT "PK_collection_feed_admission_batches" TO "PK_collection_admission_batches";
            ALTER TABLE collection_discoveries RENAME CONSTRAINT "FK_collection_feed_discoveries_collection_attempts_attempt_id" TO "FK_collection_discoveries_collection_attempts_attempt_id";
            ALTER TABLE collection_candidate_jobs RENAME CONSTRAINT "FK_collection_feed_candidate_jobs_collection_jobs_job_id" TO "FK_collection_candidate_jobs_collection_jobs_job_id";
            ALTER TABLE collection_admission_batches RENAME CONSTRAINT "FK_collection_feed_admission_batches_collection_feed_discoveri~" TO "FK_collection_admission_batches_collection_discoveries_attempt~";
            """);
        migrationBuilder.RenameIndex(name: "IX_collection_feed_candidate_jobs_job_id",
            newName: "IX_collection_candidate_jobs_job_id", table: "collection_candidate_jobs");
        migrationBuilder.RenameIndex(name: "IX_collection_feed_admission_batches_attempt_id",
            newName: "IX_collection_admission_batches_attempt_id", table: "collection_admission_batches");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE collection_admission_batches RENAME CONSTRAINT "FK_collection_admission_batches_collection_discoveries_attempt~" TO "FK_collection_feed_admission_batches_collection_feed_discoveri~";
            ALTER TABLE collection_candidate_jobs RENAME CONSTRAINT "FK_collection_candidate_jobs_collection_jobs_job_id" TO "FK_collection_feed_candidate_jobs_collection_jobs_job_id";
            ALTER TABLE collection_discoveries RENAME CONSTRAINT "FK_collection_discoveries_collection_attempts_attempt_id" TO "FK_collection_feed_discoveries_collection_attempts_attempt_id";
            ALTER TABLE collection_admission_batches RENAME CONSTRAINT "PK_collection_admission_batches" TO "PK_collection_feed_admission_batches";
            ALTER TABLE collection_candidate_jobs RENAME CONSTRAINT "PK_collection_candidate_jobs" TO "PK_collection_feed_candidate_jobs";
            ALTER TABLE collection_discoveries RENAME CONSTRAINT "PK_collection_discoveries" TO "PK_collection_feed_discoveries";
            """);
        migrationBuilder.RenameIndex(name: "IX_collection_candidate_jobs_job_id",
            newName: "IX_collection_feed_candidate_jobs_job_id", table: "collection_candidate_jobs");
        migrationBuilder.RenameIndex(name: "IX_collection_admission_batches_attempt_id",
            newName: "IX_collection_feed_admission_batches_attempt_id", table: "collection_admission_batches");
        migrationBuilder.RenameTable("collection_admission_batches", newName: "collection_feed_admission_batches");
        migrationBuilder.RenameTable("collection_candidate_jobs", newName: "collection_feed_candidate_jobs");
        migrationBuilder.RenameTable("collection_discoveries", newName: "collection_feed_discoveries");
    }
}
