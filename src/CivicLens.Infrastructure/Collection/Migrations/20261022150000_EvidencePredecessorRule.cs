using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261022150000_EvidencePredecessorRule")]
public partial class EvidencePredecessorRule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER trg_evidence_processing_release_waiters ON evidence_processing;
            DROP TRIGGER trg_collection_jobs_release_waiters ON collection_jobs;
            DROP FUNCTION civic_lens_release_predecessor_waiters();
            """);
        migrationBuilder.DropIndex("IX_evidence_processing_predecessor_attempt_id_status", "evidence_processing");
        migrationBuilder.CreateIndex("IX_evidence_processing_attempt_id", "evidence_processing", "attempt_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_evidence_processing_attempt_id", "evidence_processing");
        migrationBuilder.CreateIndex("IX_evidence_processing_predecessor_attempt_id_status", "evidence_processing",
            new[] { "predecessor_attempt_id", "status" });
        migrationBuilder.Sql("""
            CREATE FUNCTION civic_lens_release_predecessor_waiters() RETURNS trigger AS $$
            DECLARE target_attempt_id text;
            BEGIN
                IF TG_TABLE_NAME = 'collection_jobs' THEN
                    FOR target_attempt_id IN
                        SELECT attempt_id FROM collection_job_attempts
                         WHERE job_id = NEW.job_id ORDER BY attempt_id
                    LOOP
                        PERFORM pg_advisory_xact_lock(9911, hashtext(target_attempt_id));
                    END LOOP;
                    UPDATE evidence_processing waiting
                       SET status = 'Pending', predecessor_attempt_id = NULL, error_code = NULL,
                           retry_at = NULL, lease_token = NULL, lease_expires_at = NULL
                      FROM collection_job_attempts attempt
                     WHERE attempt.job_id = NEW.job_id AND waiting.predecessor_attempt_id = attempt.attempt_id
                       AND waiting.status = 'WaitingForPredecessor';
                ELSIF NEW.attempt_id = 'prepare' THEN
                    FOR target_attempt_id IN
                        SELECT attempt_id FROM collection_job_attempts
                         WHERE job_id = NEW.job_id ORDER BY attempt_id
                    LOOP
                        PERFORM pg_advisory_xact_lock(9911, hashtext(target_attempt_id));
                    END LOOP;
                    UPDATE evidence_processing waiting
                       SET status = 'Pending', predecessor_attempt_id = NULL, error_code = NULL,
                           retry_at = NULL, lease_token = NULL, lease_expires_at = NULL
                      FROM collection_job_attempts attempt
                     WHERE attempt.job_id = NEW.job_id AND waiting.predecessor_attempt_id = attempt.attempt_id
                       AND waiting.status = 'WaitingForPredecessor'
                       AND (NEW.status IN ('Blocked','Failed') OR NOT EXISTS (
                           SELECT 1 FROM evidence_processing child
                            WHERE child.job_id = attempt.job_id AND child.attempt_id = attempt.attempt_id));
                ELSE
                    PERFORM pg_advisory_xact_lock(9911, hashtext(NEW.attempt_id));
                    UPDATE evidence_processing
                       SET status = 'Pending', predecessor_attempt_id = NULL, error_code = NULL,
                           retry_at = NULL, lease_token = NULL, lease_expires_at = NULL
                     WHERE predecessor_attempt_id = NEW.attempt_id AND status = 'WaitingForPredecessor';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_evidence_processing_release_waiters
            AFTER UPDATE OF status, extraction_id ON evidence_processing
            FOR EACH ROW WHEN ((NEW.extraction_id IS NOT NULL AND OLD.extraction_id IS NULL) OR
                (NEW.status IN ('Succeeded','Blocked','Failed') AND OLD.status IS DISTINCT FROM NEW.status))
            EXECUTE FUNCTION civic_lens_release_predecessor_waiters();
            CREATE TRIGGER trg_collection_jobs_release_waiters
            AFTER UPDATE OF state ON collection_jobs
            FOR EACH ROW WHEN (NEW.state IN ('Failed','Cancelled') AND OLD.state IS DISTINCT FROM NEW.state)
            EXECUTE FUNCTION civic_lens_release_predecessor_waiters();
            UPDATE evidence_processing SET status = 'Pending', predecessor_attempt_id = NULL
             WHERE status = 'WaitingForPredecessor';
            """);
    }
}
