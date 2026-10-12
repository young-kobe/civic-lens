using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations;

[DbContext(typeof(CollectionAttemptDbContext))]
[Migration("20261012150000_StoreReviewStatusAndHasChanges")]
public partial class StoreReviewStatusAndHasChanges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE document_comparisons ADD COLUMN has_changes boolean;
            UPDATE document_comparisons
               SET has_changes = (result_json::jsonb ->> 'status') = '0'
                             AND jsonb_array_length(result_json::jsonb -> 'hunks') > 0;
            ALTER TABLE document_comparisons ALTER COLUMN has_changes SET NOT NULL;

            ALTER TABLE document_change_drafts ADD COLUMN review_status character varying(32);
            UPDATE document_change_drafts d
               SET review_status = CASE
                   WHEN latest.kind = 'Approve' THEN 'Approved'
                   WHEN latest.kind = 'RequestChanges' THEN 'ChangesRequested'
                   WHEN latest.kind = 'WithdrawApproval' THEN 'ApprovalWithdrawn'
                   WHEN EXISTS (
                       SELECT 1 FROM document_change_review_decisions blocker
                        WHERE blocker.draft_id = d.draft_id AND blocker.kind IN ('RequestChanges', 'WithdrawApproval')
                          AND NOT EXISTS (
                              SELECT 1 FROM document_change_review_decisions approval
                              CROSS JOIN LATERAL jsonb_array_elements_text(approval.decision_json::jsonb -> 'resolvedDecisionIds') AS resolved(id)
                               WHERE approval.draft_id = d.draft_id AND approval.kind = 'Approve'
                                 AND resolved.id = blocker.decision_id))
                       THEN 'AwaitingReviewWithConcerns'
                   ELSE 'AwaitingReview' END
              FROM (SELECT x.draft_id, (SELECT r.kind FROM document_change_review_decisions r
                                         WHERE r.draft_id = x.draft_id AND r.revision_number = x.current_revision_number
                                         ORDER BY r.review_state_version DESC LIMIT 1) AS kind
                      FROM document_change_drafts x) latest
             WHERE latest.draft_id = d.draft_id;
            ALTER TABLE document_change_drafts ALTER COLUMN review_status SET NOT NULL;
            ALTER TABLE document_change_drafts ADD CONSTRAINT ck_document_change_drafts_review_status
                CHECK (review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn'));

            DROP INDEX IF EXISTS ix_document_comparisons_eligible;
            CREATE INDEX ix_document_comparisons_has_changes ON document_comparisons (comparison_id) WHERE has_changes;
            CREATE INDEX ix_document_change_drafts_approved_created
                ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status = 'Approved';
            CREATE INDEX ix_document_change_drafts_needs_action_created
                ON document_change_drafts (created_at_utc_ticks DESC, draft_id DESC) WHERE review_status <> 'Approved';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_document_change_drafts_needs_action_created;
            DROP INDEX IF EXISTS ix_document_change_drafts_approved_created;
            DROP INDEX IF EXISTS ix_document_comparisons_has_changes;
            CREATE INDEX ix_document_comparisons_eligible
                ON document_comparisons (comparison_id)
                WHERE (result_json::jsonb ->> 'status') = '0'
                  AND jsonb_array_length(result_json::jsonb -> 'hunks') > 0;
            ALTER TABLE document_change_drafts DROP CONSTRAINT ck_document_change_drafts_review_status;
            ALTER TABLE document_change_drafts DROP COLUMN review_status;
            ALTER TABLE document_comparisons DROP COLUMN has_changes;
            """);
    }
}
