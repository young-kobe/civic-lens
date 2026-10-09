using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using CivicLens.Infrastructure.Collection;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261008200000_DurablePipelineWakeups")]
public partial class DurablePipelineWakeups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "predecessor_attempt_id", table: "evidence_processing",
            type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.DropCheckConstraint("ck_evidence_processing_status", "evidence_processing");
        migrationBuilder.AddCheckConstraint("ck_evidence_processing_status", "evidence_processing",
            "status IN ('Pending','Running','RetryWaiting','WaitingForPredecessor','Succeeded','Blocked','Failed')");
        migrationBuilder.CreateIndex("IX_evidence_processing_predecessor_attempt_id_status", "evidence_processing",
            new[] { "predecessor_attempt_id", "status" });
        migrationBuilder.Sql("""
            CREATE FUNCTION civic_lens_create_successful_job_preparation() RETURNS trigger AS $$
            DECLARE source_id text;
            DECLARE requested_url text;
            BEGIN
                source_id := NEW.definition_json::jsonb ->> 'sourceId';
                requested_url := NEW.definition_json::jsonb ->> 'url';
                IF source_id IS NOT NULL AND requested_url IS NOT NULL THEN
                    INSERT INTO evidence_processing (job_id, attempt_id, source_id, requested_url, stage, status,
                        fence, attempts, admitted_count, deferred_count, duplicate_count)
                    VALUES (NEW.job_id, 'prepare', source_id, requested_url, 'Preparation', 'Pending',
                        0, 0, 0, 0, 0)
                    ON CONFLICT (job_id, attempt_id) DO NOTHING;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_collection_jobs_create_preparation
            AFTER UPDATE OF state ON collection_jobs
            FOR EACH ROW WHEN (NEW.state = 'Succeeded' AND OLD.state IS DISTINCT FROM NEW.state)
            EXECUTE FUNCTION civic_lens_create_successful_job_preparation();

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
            CREATE FUNCTION civic_lens_notify_work_change() RETURNS trigger AS $$
            BEGIN
                PERFORM pg_notify('civic_lens_work', 'changed');
                IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_collection_jobs_notify
            AFTER INSERT OR DELETE OR UPDATE OF state, retry_at ON collection_jobs
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();
            CREATE TRIGGER trg_evidence_processing_notify
            AFTER INSERT OR DELETE OR UPDATE OF status, retry_at ON evidence_processing
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();
            CREATE TRIGGER trg_collection_job_origins_notify
            AFTER INSERT OR UPDATE OR DELETE ON collection_job_origins
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();
            CREATE TRIGGER trg_collection_collector_slot_notify
            AFTER INSERT OR DELETE OR UPDATE OF job_id, attempt_id ON collection_collector_slot
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();
            CREATE TRIGGER trg_document_extractions_notify
            AFTER INSERT ON document_extractions
            FOR EACH ROW EXECUTE FUNCTION civic_lens_notify_work_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_document_extractions_notify ON document_extractions;
            DROP TRIGGER IF EXISTS trg_collection_job_origins_notify ON collection_job_origins;
            DROP TRIGGER IF EXISTS trg_collection_collector_slot_notify ON collection_collector_slot;
            DROP TRIGGER IF EXISTS trg_evidence_processing_notify ON evidence_processing;
            DROP TRIGGER IF EXISTS trg_collection_jobs_notify ON collection_jobs;
            DROP TRIGGER IF EXISTS trg_evidence_processing_release_waiters ON evidence_processing;
            DROP TRIGGER IF EXISTS trg_collection_jobs_create_preparation ON collection_jobs;
            DROP TRIGGER IF EXISTS trg_collection_jobs_release_waiters ON collection_jobs;
            DROP FUNCTION IF EXISTS civic_lens_notify_work_change();
            DROP FUNCTION IF EXISTS civic_lens_release_predecessor_waiters();
            DROP FUNCTION IF EXISTS civic_lens_create_successful_job_preparation();
            """);
        migrationBuilder.DropIndex("IX_evidence_processing_predecessor_attempt_id_status", "evidence_processing");
        migrationBuilder.Sql("UPDATE evidence_processing SET status = 'Pending' WHERE status = 'WaitingForPredecessor'");
        migrationBuilder.DropCheckConstraint("ck_evidence_processing_status", "evidence_processing");
        migrationBuilder.AddCheckConstraint("ck_evidence_processing_status", "evidence_processing",
            "status IN ('Pending','Running','RetryWaiting','Succeeded','Blocked','Failed')");
        migrationBuilder.DropColumn("predecessor_attempt_id", "evidence_processing");
    }
}
