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
                nullable: true);

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
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM action_revisions v
                               JOIN action_analysis_runs r ON r.run_id = v.run_id
                               JOIN action_candidates c ON c.candidate_id = r.candidate_id
                               WHERE c.includes_subjects) THEN
                        RAISE EXCEPTION 'Cannot roll back BillSubjects: an action revision was drafted from a candidate that includes subjects. Restore a backup, or delete that revision by hand, and run the rollback again.';
                    END IF;
                END $$;
                UPDATE action_analysis_runs SET previous_run_id = NULL WHERE candidate_id IN (SELECT candidate_id FROM action_candidates WHERE includes_subjects);
                DELETE FROM action_analysis_runs WHERE candidate_id IN (SELECT candidate_id FROM action_candidates WHERE includes_subjects);
                DELETE FROM action_analysis WHERE candidate_id IN (SELECT candidate_id FROM action_candidates WHERE includes_subjects);
                DELETE FROM action_candidates WHERE includes_subjects;
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
