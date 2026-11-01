using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class ActionTextKeptReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten','textKept')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_action_analysis_runs_reason",
                table: "action_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged','codeWritten')");
        }
    }
}
