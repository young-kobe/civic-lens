using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class CodeWrittenActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_author_kind",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_run",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_author_kind",
                table: "action_revisions",
                sql: "author_kind IN ('Person','Analysis','Code')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_run",
                table: "action_revisions",
                sql: "(author_kind <> 'Person') = (run_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_author_kind",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_revisions_run",
                table: "action_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_author_kind",
                table: "action_revisions",
                sql: "author_kind IN ('Person','Analysis')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_revisions_run",
                table: "action_revisions",
                sql: "(author_kind = 'Analysis') = (run_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged')");
        }
    }
}
