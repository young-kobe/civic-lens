using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class CodeWrittenVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_author_kind",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_run",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.AddColumn<string>(
                name: "code_version",
                table: "vote_revisions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_author_kind",
                table: "vote_revisions",
                sql: "author_kind IN ('Person','Analysis','Code')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_code_version",
                table: "vote_revisions",
                sql: "code_version ~ '^[0-9a-f]{40}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_code_version_author",
                table: "vote_revisions",
                sql: "code_version IS NULL OR author_kind = 'Code'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_run",
                table: "vote_revisions",
                sql: "(author_kind <> 'Person') = (run_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged','recordRejected','codeWritten','textKept')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_author_kind",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_code_version",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_code_version_author",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_revisions_run",
                table: "vote_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs");

            migrationBuilder.DropColumn(
                name: "code_version",
                table: "vote_revisions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_author_kind",
                table: "vote_revisions",
                sql: "author_kind IN ('Person','Analysis')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_revisions_run",
                table: "vote_revisions",
                sql: "(author_kind = 'Analysis') = (run_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_vote_analysis_runs_reason",
                table: "vote_analysis_runs",
                sql: "reason IS NULL OR reason IN ('unchanged','noTrackedMember','memberVotesChanged','kindChanged','recordRejected')");
        }
    }
}
