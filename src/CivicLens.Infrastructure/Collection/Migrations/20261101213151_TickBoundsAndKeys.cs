using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class TickBoundsAndKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_versions_update",
                table: "vote_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_candidates_created",
                table: "vote_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_candidates_update",
                table: "vote_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reader_reports_received",
                table: "reader_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publication_releases_published",
                table: "publication_releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_source_schedules_due",
                table: "collection_source_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_observed_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_response_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_sent_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_stream_pages_recorded",
                table: "bill_stream_pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_stream_cursors_updated",
                table: "bill_stream_cursors");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_created",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_decided",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_update",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_candidates_created",
                table: "action_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_candidates_update",
                table: "action_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_worker_heartbeat_beat_at_utc_ticks",
                table: "worker_heartbeat",
                sql: "beat_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_versions_update_date_utc_ticks",
                table: "vote_versions",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_created_at_utc_ticks",
                table: "vote_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_review_decisions_created_at_utc_ticks",
                table: "vote_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_vote_drafts_candidate_id",
                table: "vote_drafts",
                column: "candidate_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_drafts_created_at_utc_ticks",
                table: "vote_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_drafts_last_revised_at_utc_ticks",
                table: "vote_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_candidates_created_at_utc_ticks",
                table: "vote_candidates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_candidates_update_date_utc_ticks",
                table: "vote_candidates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_finished_at_utc_ticks",
                table: "vote_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_started_at_utc_ticks",
                table: "vote_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_vote_analysis_retry_of_run_id",
                table: "vote_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_created_at_utc_ticks",
                table: "vote_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_reserved_at_utc_ticks",
                table: "vote_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_revisions_created_at_utc_ticks",
                table: "statement_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_review_decisions_created_at_utc_ticks",
                table: "statement_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_gate_labels_created_at_utc_ticks",
                table: "statement_gate_labels",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_drafts_created_at_utc_ticks",
                table: "statement_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_drafts_last_revised_at_utc_ticks",
                table: "statement_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_candidates_created_at_utc_ticks",
                table: "statement_candidates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_finished_at_utc_ticks",
                table: "statement_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_runs_started_at_utc_ticks",
                table: "statement_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_statement_analysis_retry_of_run_id",
                table: "statement_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_created_at_utc_ticks",
                table: "statement_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_statement_analysis_reserved_at_utc_ticks",
                table: "statement_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_revisions_created_at_utc_ticks",
                table: "relationship_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_review_decisions_created_at_utc_ticks",
                table: "relationship_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_drafts_created_at_utc_ticks",
                table: "relationship_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_drafts_last_revised_at_utc_ticks",
                table: "relationship_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_finished_at_utc_ticks",
                table: "relationship_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_runs_started_at_utc_ticks",
                table: "relationship_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_relationship_analysis_retry_of_run_id",
                table: "relationship_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_created_at_utc_ticks",
                table: "relationship_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_relationship_analysis_reserved_at_utc_ticks",
                table: "relationship_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reader_reports_received_at_utc_ticks",
                table: "reader_reports",
                sql: "received_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reader_reports_resolved_at_utc_ticks",
                table: "reader_reports",
                sql: "resolved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publication_releases_published_at_utc_ticks",
                table: "publication_releases",
                sql: "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_revisions_created_at_utc_ticks",
                table: "executive_action_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_review_decisions_created_at_utc_ticks",
                table: "executive_action_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_drafts_created_at_utc_ticks",
                table: "executive_action_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_executive_action_drafts_last_revised_at_utc_ticks",
                table: "executive_action_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_comparison_id",
                table: "evidence_processing",
                column: "comparison_id");

            migrationBuilder.CreateIndex(
                name: "IX_evidence_processing_extraction_id",
                table: "evidence_processing",
                column: "extraction_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidence_processing_observed_at_utc_ticks",
                table: "evidence_processing",
                sql: "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_revisions_created_at_utc_ticks",
                table: "document_change_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_review_decisions_created_at_utc_ticks",
                table: "document_change_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_drafts_created_at_utc_ticks",
                table: "document_change_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_drafts_last_revised_at_utc_ticks",
                table: "document_change_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_status_paused_until_utc_ticks",
                table: "document_change_analysis_status",
                sql: "paused_until_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_status_recorded_at_utc_ticks",
                table: "document_change_analysis_status",
                sql: "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_finished_at_utc_ticks",
                table: "document_change_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_runs_started_at_utc_ticks",
                table: "document_change_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_document_change_analysis_retry_of_run_id",
                table: "document_change_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_created_at_utc_ticks",
                table: "document_change_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_change_analysis_reserved_at_utc_ticks",
                table: "document_change_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_source_schedules_next_due_utc_ticks",
                table: "collection_source_schedules",
                sql: "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_observed_at_utc_ticks",
                table: "collection_attempts",
                sql: "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_response_last_modified_utc_ticks",
                table: "collection_attempts",
                sql: "response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_sent_last_modified_utc_ticks",
                table: "collection_attempts",
                sql: "sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_revisions_created_at_utc_ticks",
                table: "brief_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_review_decisions_created_at_utc_ticks",
                table: "brief_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_drafts_created_at_utc_ticks",
                table: "brief_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_drafts_last_revised_at_utc_ticks",
                table: "brief_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_finished_at_utc_ticks",
                table: "brief_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_runs_started_at_utc_ticks",
                table: "brief_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_brief_analysis_retry_of_run_id",
                table: "brief_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_created_at_utc_ticks",
                table: "brief_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_brief_analysis_reserved_at_utc_ticks",
                table: "brief_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_stream_pages_recorded_at_utc_ticks",
                table: "bill_stream_pages",
                sql: "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_stream_cursors_updated_at_utc_ticks",
                table: "bill_stream_cursors",
                sql: "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_created_at_utc_ticks",
                table: "bill_gates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_decided",
                table: "bill_gates",
                sql: "(decision = 'Pending') = (decided_at_utc_ticks IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_decided_at_utc_ticks",
                table: "bill_gates",
                sql: "decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_update_date_utc_ticks",
                table: "bill_gates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_created_at_utc_ticks",
                table: "action_revisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_review_decisions_created_at_utc_ticks",
                table: "action_review_decisions",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_action_drafts_candidate_id",
                table: "action_drafts",
                column: "candidate_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_drafts_created_at_utc_ticks",
                table: "action_drafts",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_drafts_last_revised_at_utc_ticks",
                table: "action_drafts",
                sql: "last_revised_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_candidates_created_at_utc_ticks",
                table: "action_candidates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_candidates_update_date_utc_ticks",
                table: "action_candidates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_finished_at_utc_ticks",
                table: "action_analysis_runs",
                sql: "finished_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_started_at_utc_ticks",
                table: "action_analysis_runs",
                sql: "started_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.CreateIndex(
                name: "IX_action_analysis_retry_of_run_id",
                table: "action_analysis",
                column: "retry_of_run_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_created_at_utc_ticks",
                table: "action_analysis",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_reserved_at_utc_ticks",
                table: "action_analysis",
                sql: "reserved_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddForeignKey(
                name: "FK_action_analysis_action_analysis_runs_retry_of_run_id",
                table: "action_analysis",
                column: "retry_of_run_id",
                principalTable: "action_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_action_drafts_action_candidates_candidate_id",
                table: "action_drafts",
                column: "candidate_id",
                principalTable: "action_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_brief_analysis_brief_analysis_runs_retry_of_run_id",
                table: "brief_analysis",
                column: "retry_of_run_id",
                principalTable: "brief_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_document_change_analysis_document_change_analysis_runs_retr~",
                table: "document_change_analysis",
                column: "retry_of_run_id",
                principalTable: "document_change_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_processing_document_comparisons_comparison_id",
                table: "evidence_processing",
                column: "comparison_id",
                principalTable: "document_comparisons",
                principalColumn: "comparison_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_processing_document_extractions_extraction_id",
                table: "evidence_processing",
                column: "extraction_id",
                principalTable: "document_extractions",
                principalColumn: "extraction_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_relationship_analysis_relationship_analysis_runs_retry_of_r~",
                table: "relationship_analysis",
                column: "retry_of_run_id",
                principalTable: "relationship_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_statement_analysis_statement_analysis_runs_retry_of_run_id",
                table: "statement_analysis",
                column: "retry_of_run_id",
                principalTable: "statement_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_analysis_vote_analysis_runs_retry_of_run_id",
                table: "vote_analysis",
                column: "retry_of_run_id",
                principalTable: "vote_analysis_runs",
                principalColumn: "run_id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vote_drafts_vote_candidates_candidate_id",
                table: "vote_drafts",
                column: "candidate_id",
                principalTable: "vote_candidates",
                principalColumn: "candidate_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_action_analysis_action_analysis_runs_retry_of_run_id",
                table: "action_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_action_drafts_action_candidates_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_brief_analysis_brief_analysis_runs_retry_of_run_id",
                table: "brief_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_document_change_analysis_document_change_analysis_runs_retr~",
                table: "document_change_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_processing_document_comparisons_comparison_id",
                table: "evidence_processing");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_processing_document_extractions_extraction_id",
                table: "evidence_processing");

            migrationBuilder.DropForeignKey(
                name: "FK_relationship_analysis_relationship_analysis_runs_retry_of_r~",
                table: "relationship_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_statement_analysis_statement_analysis_runs_retry_of_run_id",
                table: "statement_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_analysis_vote_analysis_runs_retry_of_run_id",
                table: "vote_analysis");

            migrationBuilder.DropForeignKey(
                name: "FK_vote_drafts_vote_candidates_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_worker_heartbeat_beat_at_utc_ticks",
                table: "worker_heartbeat");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_versions_update_date_utc_ticks",
                table: "vote_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_created_at_utc_ticks",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_review_decisions_created_at_utc_ticks",
                table: "vote_review_decisions");

            migrationBuilder.DropIndex(
                name: "IX_vote_drafts_candidate_id",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_drafts_created_at_utc_ticks",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_drafts_last_revised_at_utc_ticks",
                table: "vote_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_candidates_created_at_utc_ticks",
                table: "vote_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_candidates_update_date_utc_ticks",
                table: "vote_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_finished_at_utc_ticks",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_started_at_utc_ticks",
                table: "vote_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_vote_analysis_retry_of_run_id",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_created_at_utc_ticks",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_reserved_at_utc_ticks",
                table: "vote_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_revisions_created_at_utc_ticks",
                table: "statement_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_review_decisions_created_at_utc_ticks",
                table: "statement_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_gate_labels_created_at_utc_ticks",
                table: "statement_gate_labels");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_drafts_created_at_utc_ticks",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_drafts_last_revised_at_utc_ticks",
                table: "statement_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_candidates_created_at_utc_ticks",
                table: "statement_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_finished_at_utc_ticks",
                table: "statement_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_runs_started_at_utc_ticks",
                table: "statement_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_statement_analysis_retry_of_run_id",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_created_at_utc_ticks",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_statement_analysis_reserved_at_utc_ticks",
                table: "statement_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_revisions_created_at_utc_ticks",
                table: "relationship_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_review_decisions_created_at_utc_ticks",
                table: "relationship_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_drafts_created_at_utc_ticks",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_drafts_last_revised_at_utc_ticks",
                table: "relationship_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_finished_at_utc_ticks",
                table: "relationship_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_runs_started_at_utc_ticks",
                table: "relationship_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_relationship_analysis_retry_of_run_id",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_created_at_utc_ticks",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_relationship_analysis_reserved_at_utc_ticks",
                table: "relationship_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reader_reports_received_at_utc_ticks",
                table: "reader_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reader_reports_resolved_at_utc_ticks",
                table: "reader_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_publication_releases_published_at_utc_ticks",
                table: "publication_releases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_revisions_created_at_utc_ticks",
                table: "executive_action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_review_decisions_created_at_utc_ticks",
                table: "executive_action_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_drafts_created_at_utc_ticks",
                table: "executive_action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_executive_action_drafts_last_revised_at_utc_ticks",
                table: "executive_action_drafts");

            migrationBuilder.DropIndex(
                name: "IX_evidence_processing_comparison_id",
                table: "evidence_processing");

            migrationBuilder.DropIndex(
                name: "IX_evidence_processing_extraction_id",
                table: "evidence_processing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidence_processing_observed_at_utc_ticks",
                table: "evidence_processing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_revisions_created_at_utc_ticks",
                table: "document_change_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_review_decisions_created_at_utc_ticks",
                table: "document_change_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_drafts_created_at_utc_ticks",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_drafts_last_revised_at_utc_ticks",
                table: "document_change_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_status_paused_until_utc_ticks",
                table: "document_change_analysis_status");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_status_recorded_at_utc_ticks",
                table: "document_change_analysis_status");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_finished_at_utc_ticks",
                table: "document_change_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_runs_started_at_utc_ticks",
                table: "document_change_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_document_change_analysis_retry_of_run_id",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_created_at_utc_ticks",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_document_change_analysis_reserved_at_utc_ticks",
                table: "document_change_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_source_schedules_next_due_utc_ticks",
                table: "collection_source_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_observed_at_utc_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_response_last_modified_utc_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_collection_attempts_sent_last_modified_utc_ticks",
                table: "collection_attempts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_revisions_created_at_utc_ticks",
                table: "brief_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_review_decisions_created_at_utc_ticks",
                table: "brief_review_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_drafts_created_at_utc_ticks",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_drafts_last_revised_at_utc_ticks",
                table: "brief_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_finished_at_utc_ticks",
                table: "brief_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_runs_started_at_utc_ticks",
                table: "brief_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_brief_analysis_retry_of_run_id",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_created_at_utc_ticks",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_brief_analysis_reserved_at_utc_ticks",
                table: "brief_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_stream_pages_recorded_at_utc_ticks",
                table: "bill_stream_pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_stream_cursors_updated_at_utc_ticks",
                table: "bill_stream_cursors");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_created_at_utc_ticks",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_decided",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_decided_at_utc_ticks",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_update_date_utc_ticks",
                table: "bill_gates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_created_at_utc_ticks",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_review_decisions_created_at_utc_ticks",
                table: "action_review_decisions");

            migrationBuilder.DropIndex(
                name: "IX_action_drafts_candidate_id",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_drafts_created_at_utc_ticks",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_drafts_last_revised_at_utc_ticks",
                table: "action_drafts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_candidates_created_at_utc_ticks",
                table: "action_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_candidates_update_date_utc_ticks",
                table: "action_candidates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_finished_at_utc_ticks",
                table: "action_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_started_at_utc_ticks",
                table: "action_analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_action_analysis_retry_of_run_id",
                table: "action_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_created_at_utc_ticks",
                table: "action_analysis");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_reserved_at_utc_ticks",
                table: "action_analysis");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_versions_update",
                table: "vote_versions",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_candidates_created",
                table: "vote_candidates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_candidates_update",
                table: "vote_candidates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reader_reports_received",
                table: "reader_reports",
                sql: "received_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_publication_releases_published",
                table: "publication_releases",
                sql: "published_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_source_schedules_due",
                table: "collection_source_schedules",
                sql: "next_due_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_observed_ticks",
                table: "collection_attempts",
                sql: "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_response_ticks",
                table: "collection_attempts",
                sql: "response_last_modified_utc_ticks IS NULL OR response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_collection_attempts_sent_ticks",
                table: "collection_attempts",
                sql: "sent_last_modified_utc_ticks IS NULL OR sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_stream_pages_recorded",
                table: "bill_stream_pages",
                sql: "recorded_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_stream_cursors_updated",
                table: "bill_stream_cursors",
                sql: "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_created",
                table: "bill_gates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_decided",
                table: "bill_gates",
                sql: "(decision = 'Pending') = (decided_at_utc_ticks IS NULL) AND (decided_at_utc_ticks IS NULL OR decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_update",
                table: "bill_gates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_candidates_created",
                table: "action_candidates",
                sql: "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_candidates_update",
                table: "action_candidates",
                sql: "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
        }
    }
}
