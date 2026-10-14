using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261022120000_EvidencePredecessorLockKey")]
public partial class EvidencePredecessorLockKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION civic_lens_release_predecessor_waiters() RETURNS trigger AS $$
            DECLARE target_attempt_id text;
            BEGIN
                IF TG_TABLE_NAME = 'collection_jobs' THEN
                    FOR target_attempt_id IN
                        SELECT attempt_id FROM collection_job_attempts
                         WHERE job_id = NEW.job_id ORDER BY attempt_id
                    LOOP
                        PERFORM pg_advisory_xact_lock(hashtextextended('civic-lens-evidence-predecessor:' || target_attempt_id, 0));
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
                        PERFORM pg_advisory_xact_lock(hashtextextended('civic-lens-evidence-predecessor:' || target_attempt_id, 0));
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
                    PERFORM pg_advisory_xact_lock(hashtextextended('civic-lens-evidence-predecessor:' || NEW.attempt_id, 0));
                    UPDATE evidence_processing
                       SET status = 'Pending', predecessor_attempt_id = NULL, error_code = NULL,
                           retry_at = NULL, lease_token = NULL, lease_expires_at = NULL
                     WHERE predecessor_attempt_id = NEW.attempt_id AND status = 'WaitingForPredecessor';
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION civic_lens_release_predecessor_waiters() RETURNS trigger AS $$
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
            """);
    }
}
