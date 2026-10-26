using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CivicLens.Infrastructure.Collection.Migrations
{
    /// <inheritdoc />
    public partial class CollectionJobPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collection_jobs_active_created_at_job_id",
                table: "collection_jobs");

            migrationBuilder.AddColumn<short>(
                name: "priority",
                table: "collection_jobs",
                type: "smallint",
                nullable: false,
                defaultValue: (short)3);

            migrationBuilder.CreateIndex(
                name: "ix_collection_jobs_active_claim",
                table: "collection_jobs",
                columns: new[] { "priority", "created_at", "job_id" },
                filter: "state IN ('Pending','Running','WaitingToRetry')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_priority",
                table: "collection_jobs",
                sql: "priority BETWEEN 1 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_collection_jobs_active_claim",
                table: "collection_jobs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_job_priority",
                table: "collection_jobs");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "collection_jobs");

            migrationBuilder.CreateIndex(
                name: "ix_collection_jobs_active_created_at_job_id",
                table: "collection_jobs",
                columns: new[] { "created_at", "job_id" },
                filter: "state IN ('Pending','Running','WaitingToRetry')");
        }
    }
}
