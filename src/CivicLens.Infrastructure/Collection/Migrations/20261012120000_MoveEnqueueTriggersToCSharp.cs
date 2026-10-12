using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012120000_MoveEnqueueTriggersToCSharp")]
public partial class MoveEnqueueTriggersToCSharp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_document_comparisons_register_analysis ON document_comparisons;
            DROP FUNCTION IF EXISTS civic_lens_register_document_change_analysis();
            DROP TRIGGER IF EXISTS trg_collection_jobs_create_preparation ON collection_jobs;
            DROP FUNCTION IF EXISTS civic_lens_create_successful_job_preparation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE FUNCTION civic_lens_register_document_change_analysis() RETURNS trigger AS $$
            BEGIN
                IF (NEW.result_json::jsonb ->> 'status') = '0' AND jsonb_array_length(NEW.result_json::jsonb -> 'hunks') > 0 THEN
                    INSERT INTO document_change_analysis (comparison_id, task, status, fence, attempts,
                        reserved_tokens, created_at_utc_ticks)
                    VALUES (NEW.comparison_id, 'draft', 'Pending', 0, 0, 0,
                        (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint)
                    ON CONFLICT (comparison_id, task) DO NOTHING;
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_document_comparisons_register_analysis
            AFTER INSERT ON document_comparisons
            FOR EACH ROW EXECUTE FUNCTION civic_lens_register_document_change_analysis();

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
            """);
    }
}
