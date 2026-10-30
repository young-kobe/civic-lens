using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class BillSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_pages_role",
                table: "bill_pages");

            migrationBuilder.DropIndex(
                name: "ux_action_candidates_detail_job",
                table: "action_candidates");

            migrationBuilder.AddColumn<int>(
                name: "subject_count",
                table: "bill_gates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "includes_subjects",
                table: "action_candidates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_pages_role",
                table: "bill_pages",
                sql: "role IN ('Detail','Cosponsors','Actions','Summaries','Subjects')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_gates_subject_count",
                table: "bill_gates",
                sql: "subject_count >= 0");

            migrationBuilder.CreateIndex(
                name: "ux_action_candidates_detail_job",
                table: "action_candidates",
                columns: new[] { "detail_job_id", "includes_summaries", "includes_subjects" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM action_analysis_runs WHERE candidate_id IN (SELECT candidate_id FROM action_candidates WHERE includes_subjects)
                    AND run_id NOT IN (SELECT run_id FROM action_revisions WHERE run_id IS NOT NULL);
                DELETE FROM action_analysis WHERE candidate_id IN (SELECT candidate_id FROM action_candidates WHERE includes_subjects)
                    AND NOT EXISTS (SELECT 1 FROM action_analysis_runs r WHERE r.candidate_id = action_analysis.candidate_id);
                DELETE FROM action_candidates WHERE includes_subjects
                    AND NOT EXISTS (SELECT 1 FROM action_analysis a WHERE a.candidate_id = action_candidates.candidate_id);
                DELETE FROM bill_pages WHERE role = 'Subjects';
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_pages_role",
                table: "bill_pages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_bill_gates_subject_count",
                table: "bill_gates");

            migrationBuilder.DropIndex(
                name: "ux_action_candidates_detail_job",
                table: "action_candidates");

            migrationBuilder.DropColumn(
                name: "subject_count",
                table: "bill_gates");

            migrationBuilder.DropColumn(
                name: "includes_subjects",
                table: "action_candidates");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bill_pages_role",
                table: "bill_pages",
                sql: "role IN ('Detail','Cosponsors','Actions','Summaries')");

            migrationBuilder.CreateIndex(
                name: "ux_action_candidates_detail_job",
                table: "action_candidates",
                columns: new[] { "detail_job_id", "includes_summaries" },
                unique: true);
        }
    }
}
