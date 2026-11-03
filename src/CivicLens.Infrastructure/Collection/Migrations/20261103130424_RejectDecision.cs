using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class RejectDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_drafts_review_status",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_drafts_review_status",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_drafts_review_status",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_drafts_review_status",
                table: "executive_action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_drafts_review_status",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_drafts_review_status",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_drafts_review_status",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "vote_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refused_candidate_id",
                table: "vote_drafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "statement_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "relationship_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "executive_action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "document_change_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "brief_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "action_review_decisions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refused_candidate_id",
                table: "action_drafts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_reason_code",
                table: "vote_review_decisions",
                sql: "reason_code IN ('InaccurateSummary', 'Duplicate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_reason_code_kind",
                table: "vote_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_vote_drafts_refused_candidate_id",
                table: "vote_drafts",
                column: "refused_candidate_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_drafts_review_status",
                table: "vote_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged','recordRejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_reason_code",
                table: "statement_review_decisions",
                sql: "reason_code IN ('NotAStatement', 'WrongSpeaker', 'IndexPage', 'LowSubstance', 'InaccurateSummary', 'Duplicate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_reason_code_kind",
                table: "statement_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "candidate_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_drafts_review_status",
                table: "statement_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','NotDrafted','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_reason_code",
                table: "relationship_review_decisions",
                sql: "reason_code IN ('NotRelated', 'Duplicate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_reason_code_kind",
                table: "relationship_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_drafts_review_status",
                table: "relationship_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_reason_code",
                table: "executive_action_review_decisions",
                sql: "reason_code IN ('InaccurateSummary', 'Duplicate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_reason_code_kind",
                table: "executive_action_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_drafts_review_status",
                table: "executive_action_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_reason_code",
                table: "document_change_review_decisions",
                sql: "reason_code IN ('InaccurateSummary', 'Duplicate', 'LowSubstance')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_reason_code_kind",
                table: "document_change_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "draft_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_drafts_review_status",
                table: "document_change_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_reason_code",
                table: "brief_review_decisions",
                sql: "reason_code IN ('InaccurateSummary', 'Duplicate', 'LowSubstance')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_reason_code_kind",
                table: "brief_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_drafts_review_status",
                table: "brief_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval', 'Reject')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_reason_code",
                table: "action_review_decisions",
                sql: "reason_code IN ('InaccurateSummary', 'Duplicate')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_reason_code_kind",
                table: "action_review_decisions",
                sql: "(kind = 'Reject') = (reason_code IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved' AND review_status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_action_drafts_refused_candidate_id",
                table: "action_drafts",
                column: "refused_candidate_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_drafts_review_status",
                table: "action_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept','recordRejected')");

            migrationBuilder.AddForeignKey(
                name: "FK_action_drafts_action_candidates_refused_candidate_id",
                table: "action_drafts",
                column: "refused_candidate_id",
                principalTable: "action_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_drafts_vote_candidates_refused_candidate_id",
                table: "vote_drafts",
                column: "refused_candidate_id",
                principalTable: "vote_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_drafts_action_candidates_refused_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_drafts_vote_candidates_refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_reason_code",
                table: "vote_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_reason_code_kind",
                table: "vote_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts");

            migrationBuilder.DropIndex(
                name: "IX_vote_drafts_refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_drafts_review_status",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_reason_code",
                table: "statement_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_reason_code_kind",
                table: "statement_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_drafts_review_status",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_reason_code",
                table: "relationship_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_reason_code_kind",
                table: "relationship_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_drafts_review_status",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_reason_code",
                table: "executive_action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_reason_code_kind",
                table: "executive_action_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_drafts_review_status",
                table: "executive_action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_reason_code",
                table: "document_change_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_reason_code_kind",
                table: "document_change_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_drafts_review_status",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_reason_code",
                table: "brief_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_reason_code_kind",
                table: "brief_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_drafts_review_status",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_reason_code",
                table: "action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_reason_code_kind",
                table: "action_review_decisions");

            migrationBuilder.DropIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts");

            migrationBuilder.DropIndex(
                name: "IX_action_drafts_refused_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_drafts_review_status",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "vote_review_decisions");

            migrationBuilder.DropColumn(
                name: "refused_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "statement_review_decisions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "relationship_review_decisions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "executive_action_review_decisions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "document_change_review_decisions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "brief_review_decisions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "action_review_decisions");

            migrationBuilder.DropColumn(
                name: "refused_candidate_id",
                table: "action_drafts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_kind",
                table: "vote_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_vote_drafts_needs_action_revised",
                table: "vote_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_drafts_review_status",
                table: "vote_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_kind",
                table: "statement_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_statement_drafts_needs_action_revised",
                table: "statement_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "candidate_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_drafts_review_status",
                table: "statement_drafts",
                sql: "review_status IN ('NotDrafted','AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_kind",
                table: "relationship_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_relationship_drafts_needs_action_revised",
                table: "relationship_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_drafts_review_status",
                table: "relationship_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_kind",
                table: "executive_action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_executive_action_drafts_needs_action_revised",
                table: "executive_action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_drafts_review_status",
                table: "executive_action_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_kind",
                table: "document_change_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_document_change_drafts_needs_action_revised",
                table: "document_change_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "draft_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_drafts_review_status",
                table: "document_change_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_kind",
                table: "brief_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_brief_drafts_needs_action_revised",
                table: "brief_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_drafts_review_status",
                table: "brief_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_kind",
                table: "action_review_decisions",
                sql: "kind IN ('Approve', 'RequestChanges', 'WithdrawApproval')");

            migrationBuilder.CreateIndex(
                name: "ix_action_drafts_needs_action_revised",
                table: "action_drafts",
                columns: new[] { "last_revised_at_utc_ticks", "record_id" },
                descending: new bool[0],
                filter: "review_status <> 'Approved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_drafts_review_status",
                table: "action_drafts",
                sql: "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept')");
        }
    }
}
