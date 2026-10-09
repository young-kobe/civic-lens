using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261009130000_KeysetReads")]
public partial class KeysetReads : Migration
{
    // Ticks run from 0001-01-01 and the Unix epoch is 621355968000000000 ticks after that.
    // JSON status 0 and decision kinds 0..2 are the enum numbers stored by the review and comparison writers.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE document_change_revisions ADD COLUMN created_at_utc_ticks bigint;
            UPDATE document_change_revisions
               SET created_at_utc_ticks = (extract(epoch FROM (revision_json::jsonb ->> 'createdAtUtc')::timestamptz)
                                           * 10000000)::bigint + 621355968000000000;
            ALTER TABLE document_change_revisions ALTER COLUMN created_at_utc_ticks SET NOT NULL;

            ALTER TABLE document_change_review_decisions ADD COLUMN created_at_utc_ticks bigint;
            ALTER TABLE document_change_review_decisions ADD COLUMN kind text;
            UPDATE document_change_review_decisions
               SET created_at_utc_ticks = (extract(epoch FROM (decision_json::jsonb ->> 'createdAtUtc')::timestamptz)
                                           * 10000000)::bigint + 621355968000000000,
                   kind = CASE decision_json::jsonb ->> 'kind'
                       WHEN '0' THEN 'Approve' WHEN 'Approve' THEN 'Approve'
                       WHEN '1' THEN 'RequestChanges' WHEN 'RequestChanges' THEN 'RequestChanges'
                       WHEN '2' THEN 'WithdrawApproval' WHEN 'WithdrawApproval' THEN 'WithdrawApproval'
                   END;
            ALTER TABLE document_change_review_decisions ALTER COLUMN created_at_utc_ticks SET NOT NULL;
            ALTER TABLE document_change_review_decisions ALTER COLUMN kind SET NOT NULL;

            CREATE INDEX ix_document_change_drafts_created
                ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC);
            CREATE INDEX ix_document_change_revisions_created
                ON document_change_revisions (created_at_utc_ticks DESC) WHERE revision_number > 1;
            CREATE INDEX ix_document_change_decisions_created
                ON document_change_review_decisions (created_at_utc_ticks DESC);
            CREATE INDEX ix_document_comparisons_eligible
                ON document_comparisons (comparison_id)
                WHERE (result_json::jsonb ->> 'status') = '0'
                  AND jsonb_array_length(result_json::jsonb -> 'hunks') > 0;
            CREATE INDEX ix_collection_jobs_created_desc ON collection_jobs (created_at DESC, job_id);
            CREATE INDEX ix_collection_jobs_source_created
                ON collection_jobs ((definition_json::jsonb ->> 'sourceId'), created_at DESC);
            CREATE INDEX ix_evidence_processing_changed
                ON evidence_processing (observed_at_utc_ticks DESC) WHERE outcome = 'Changed';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_evidence_processing_changed;
            DROP INDEX IF EXISTS ix_collection_jobs_source_created;
            DROP INDEX IF EXISTS ix_collection_jobs_created_desc;
            DROP INDEX IF EXISTS ix_document_comparisons_eligible;
            DROP INDEX IF EXISTS ix_document_change_decisions_created;
            DROP INDEX IF EXISTS ix_document_change_revisions_created;
            DROP INDEX IF EXISTS ix_document_change_drafts_created;
            ALTER TABLE document_change_review_decisions DROP COLUMN kind;
            ALTER TABLE document_change_review_decisions DROP COLUMN created_at_utc_ticks;
            ALTER TABLE document_change_revisions DROP COLUMN created_at_utc_ticks;
            """);
    }
}
