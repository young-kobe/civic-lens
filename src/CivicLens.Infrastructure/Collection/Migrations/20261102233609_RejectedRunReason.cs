using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class RejectedRunReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged','recordRejected')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept','recordRejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept')");
        }
    }
}
