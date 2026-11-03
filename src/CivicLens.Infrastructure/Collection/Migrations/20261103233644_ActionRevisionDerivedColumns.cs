using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionRevisionDerivedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions");

            migrationBuilder.AddColumn<int>(
                name: "cosponsor_count",
                table: "action_revisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "event_on",
                table: "action_revisions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_action_revisions_event_on",
                table: "action_revisions",
                column: "event_on");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions",
                sql: "num_nulls(headline, summary, text_origin, source_id, congress, bill_type, bill_number, candidate_id, candidate_update_utc_ticks, candidate_includes_summaries, candidate_includes_subjects, fingerprint, bill_title, issue_revision_id, issue_candidates_recorded, event_on, cosponsor_count) IN (0, 17)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_cosponsor_count",
                table: "action_revisions",
                sql: "cosponsor_count >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_action_revisions_event_on",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_cosponsor_count",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "cosponsor_count",
                table: "action_revisions");

            migrationBuilder.DropColumn(
                name: "event_on",
                table: "action_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_columns_written",
                table: "action_revisions",
                sql: "num_nulls(headline, summary, text_origin, source_id, congress, bill_type, bill_number, candidate_id, candidate_update_utc_ticks, candidate_includes_summaries, candidate_includes_subjects, fingerprint, bill_title, issue_revision_id, issue_candidates_recorded) IN (0, 15)");
        }
    }
}
